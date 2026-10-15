using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AdjustCropToBreakLines
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AdjustCropToBreakLinesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Получаем выбранные элементы
            var selectedIds = uidoc.Selection.GetElementIds();

            List<View> viewsToProcess = new List<View>();

            foreach (ElementId id in selectedIds)
            {
                Element element = doc.GetElement(id);

                if (element is Viewport viewport)
                {
                    View view = doc.GetElement(viewport.ViewId) as View;
                    if (view != null && !view.IsTemplate)
                        viewsToProcess.Add(view);
                }
                else if (element is View view && !view.IsTemplate)
                {
                    viewsToProcess.Add(view);
                }
            }

            if (viewsToProcess.Count == 0)
            {
                TaskDialog.Show("Ошибка",
                    "Пожалуйста, выберите:\n" +
                    "- Видовые экраны на листе\n" +
                    "- Или сами виды в Project Browser");
                return Result.Failed;
            }

            // Показываем WPF окно для ввода параметров
            var settingsWindow = new SettingsWindow(uiapp, viewsToProcess.Count);
            bool? result = settingsWindow.ShowDialog();

            if (result != true)
                return Result.Cancelled;

            // ИСПРАВЛЕНО: используем новые имена свойств
            double offsetMM = settingsWindow.OffsetValueMM;
            bool useBreakLines = settingsWindow.UseBreakLinesValue;
            bool useModelElements = settingsWindow.UseModelElementsValue;

            int successCount = 0;
            int failCount = 0;
            string failList = "";

            using (Transaction trans = new Transaction(doc, "Подогнать контур обрезки"))
            {
                trans.Start();

                foreach (View view in viewsToProcess)
                {
                    if (AdjustCropBox(view, offsetMM, useBreakLines, useModelElements, out string errorMsg))
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        failList += $"\n- {view.Name}: {errorMsg}";
                    }
                }

                trans.Commit();
            }

            TaskDialog.Show("Результат",
                $"Обработано видов: {viewsToProcess.Count}\n" +
                $"Успешно: {successCount}\n" +
                $"Не удалось: {failCount}\n" +
                $"{failList}");

            return Result.Succeeded;
        }

        private bool AdjustCropBox(View view, double offsetMM, bool useBreakLines, bool useModelElements, out string errorMessage)
        {
            errorMessage = "";
            Document doc = view.Document;

            try
            {
                if (!view.CropBoxActive)
                    view.CropBoxActive = true;

                BoundingBoxXYZ currentCropBox = view.CropBox;
                Transform transform = currentCropBox.Transform;

                List<BoundingBoxXYZ> breakLineBoxes = new List<BoundingBoxXYZ>();
                List<BoundingBoxXYZ> modelBoxes = new List<BoundingBoxXYZ>();

                if (useBreakLines)
                    breakLineBoxes = GetBreakLineBoundingBoxes(view);

                if (useModelElements)
                    modelBoxes = GetModelElementsBoundingBoxes(view);

                if (modelBoxes.Count == 0 && breakLineBoxes.Count == 0)
                {
                    errorMessage = "Не найдены элементы для обрезки";
                    return false;
                }

                // Собираем все точки из bounding box'ов
                List<XYZ> allPoints = new List<XYZ>();

                foreach (var box in modelBoxes)
                {
                    AddBoundingBoxCorners(box, transform, allPoints);
                }

                foreach (var box in breakLineBoxes)
                {
                    AddBoundingBoxCorners(box, transform, allPoints);
                }

                if (allPoints.Count == 0)
                {
                    errorMessage = "Не удалось получить геометрию элементов";
                    return false;
                }

                // Находим границы всех элементов
                double minX = allPoints.Min(p => p.X);
                double maxX = allPoints.Max(p => p.X);
                double minY = allPoints.Min(p => p.Y);
                double maxY = allPoints.Max(p => p.Y);
                double minZ = allPoints.Min(p => p.Z);
                double maxZ = allPoints.Max(p => p.Z);

                double finalMinX = minX;
                double finalMaxX = maxX;
                double finalMinY = minY;
                double finalMaxY = maxY;
                double finalMinZ = minZ;
                double finalMaxZ = maxZ;

                // Если есть линии обрыва, используем их для корректировки границ
                if (useBreakLines && breakLineBoxes.Count > 0)
                {
                    // Находим границы линий обрыва
                    List<XYZ> breakPoints = new List<XYZ>();
                    foreach (var box in breakLineBoxes)
                    {
                        AddBoundingBoxCorners(box, transform, breakPoints);
                    }

                    if (breakPoints.Count > 0)
                    {
                        double breakMinX = breakPoints.Min(p => p.X);
                        double breakMaxX = breakPoints.Max(p => p.X);
                        double breakMinY = breakPoints.Min(p => p.Y);
                        double breakMaxY = breakPoints.Max(p => p.Y);

                        // Используем линии обрыва для всех сторон
                        finalMinX = breakMinX;
                        finalMaxX = breakMaxX;
                        finalMinY = breakMinY;
                        finalMaxY = breakMaxY;
                    }
                }

                // Добавляем отступ
                double offsetFeet = offsetMM / 304.8;
                finalMinX -= offsetFeet;
                finalMaxX += offsetFeet;
                finalMinY -= offsetFeet;
                finalMaxY += offsetFeet;
                finalMinZ -= offsetFeet;
                finalMaxZ += offsetFeet;

                // Создаем новый bounding box
                BoundingBoxXYZ newBbox = new BoundingBoxXYZ();
                newBbox.Enabled = true;
                newBbox.Transform = transform;
                newBbox.Min = new XYZ(finalMinX, finalMinY, finalMinZ);
                newBbox.Max = new XYZ(finalMaxX, finalMaxY, finalMaxZ);

                view.CropBox = newBbox;

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private void AddBoundingBoxCorners(BoundingBoxXYZ box, Transform transform, List<XYZ> points)
        {
            if (box == null) return;

            XYZ min = box.Min;
            XYZ max = box.Max;

            if (min.X < -10000 || min.Y < -10000 || max.X > 100000 || max.Y > 100000) return;

            XYZ[] corners = new XYZ[]
            {
                new XYZ(min.X, min.Y, min.Z),
                new XYZ(min.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(max.X, max.Y, min.Z),
                new XYZ(max.X, max.Y, max.Z)
            };

            foreach (XYZ corner in corners)
            {
                try
                {
                    points.Add(transform.Inverse.OfPoint(corner));
                }
                catch { }
            }
        }

        private List<BoundingBoxXYZ> GetBreakLineBoundingBoxes(View view)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            Document doc = view.Document;

            // Собираем все семейства категории "Элемент узлов"
            FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id);
            var detailComponents = collector
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            foreach (FamilyInstance component in detailComponents)
            {
                // Получаем bounding box элемента целиком
                BoundingBoxXYZ bbox = component.get_BoundingBox(view);
                if (bbox != null)
                {
                    boxes.Add(bbox);
                }
            }

            return boxes;
        }

        private List<BoundingBoxXYZ> GetModelElementsBoundingBoxes(View view)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            Document doc = view.Document;

            // Категории элементов модели
            List<BuiltInCategory> modelCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Columns
            };

            FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id);
            List<Element> visibleElements = new List<Element>();

            foreach (var category in modelCategories)
            {
                var elements = collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToList();
                visibleElements.AddRange(elements);
            }

            // Добавляем стены отдельно
            var walls = collector
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .ToList();
            visibleElements.AddRange(walls);

            foreach (Element element in visibleElements)
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(view);
                if (bbox != null)
                {
                    boxes.Add(bbox);
                }
            }

            return boxes;
        }
    }
}