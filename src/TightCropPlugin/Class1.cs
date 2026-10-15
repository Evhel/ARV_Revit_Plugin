using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace AdjustCropToBreakLines
{
    [Transaction(TransactionMode.Manual)]
    public class AdjustCropToBreakLinesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                var views = uidoc.Selection.GetElementIds().Select(doc.GetElement).Select(e => e is Viewport vp ? doc.GetElement(vp.ViewId) : e)
                    .OfType<View>().Where(v => !v.IsTemplate).GroupBy(v => v.Id.IntegerValue).Select(g => g.First()).ToList();
                if (views.Count == 0)
                {
                    TaskDialog.Show("Обрезка видов", "Выберите виды в диспетчере проекта или видовые экраны на листе.");
                    return Result.Cancelled;
                }
                var window = new SettingsWindow(data.Application, views.Count);
                if (window.ShowDialog() != true) return Result.Cancelled;
                int changed = 0;
                var errors = new List<string>();
                using (var transaction = new Transaction(doc, "Обрезка видов"))
                {
                    transaction.Start();
                    foreach (var view in views)
                    {
                        using (var item = new SubTransaction(doc))
                        {
                            item.Start();
                            try
                            {
                                AdjustCropBox(view, window.OffsetValueMM, window.UseBreakLinesValue, window.UseModelElementsValue);
                                doc.Regenerate();
                                if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Изменение не подтверждено.");
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                                errors.Add(view.Name + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0) { transaction.RollBack(); TaskDialog.Show("Обрезка видов", string.Join("\n", errors)); return Result.Cancelled; }
                    RevitSupport.Commit(transaction);
                }
                TaskDialog.Show("Обрезка видов", $"Обработано: {changed}. Пропущено: {errors.Count}.\n" + string.Join("\n", errors));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        public static void AdjustCropBox(View view, double offsetMM, bool useBreakLines, bool useModelElements)
        {
            if (!(view is ViewPlan) && !(view is ViewSection))
                throw new InvalidOperationException("Поддерживаются планы, разрезы, фасады и узлы с рамкой обрезки.");
            var original = view.CropBox;
            if (original == null) throw new InvalidOperationException("У вида нет рамки обрезки.");
            using (var manager = view.GetCropRegionShapeManager())
                if (manager.ShapeSet || manager.NumberOfSplitRegions > 1)
                    throw new InvalidOperationException("Непрямоугольная или разделённая обрезка сохранена без изменений.");
            var doc = view.Document;
            var modelBoxes = new List<BoundingBoxXYZ>();
            var breakBoxes = new List<BoundingBoxXYZ>();
            if (useModelElements)
            {
                var categories = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFoundation,
                    BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Columns,
                    BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows
                };
                modelBoxes = new FilteredElementCollector(doc, view.Id).WherePasses(new ElementMulticategoryFilter(categories))
                    .WhereElementIsNotElementType().Select(e => e.get_BoundingBox(view)).Where(b => b != null).ToList();
            }
            if (useBreakLines)
            {
                breakBoxes = new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_DetailComponents).Cast<FamilyInstance>()
                    .Where(e => IsBreakLine(e.Symbol.FamilyName + " " + e.Name))
                    .Select(e => e.get_BoundingBox(view)).Where(b => b != null).ToList();
            }
            // При наличии распознанных линий обрыва именно они задают границы.
            var boxes = breakBoxes.Count > 0 ? breakBoxes : modelBoxes;
            var points = boxes.SelectMany(b => CornersInView(b, original.Transform)).ToList();
            if (points.Count == 0) throw new InvalidOperationException("В текущей видимой области не найдены выбранные источники границ.");
            double offset = offsetMM / 304.8;
            double minX = points.Min(p => p.X) - offset, maxX = points.Max(p => p.X) + offset;
            double minY = points.Min(p => p.Y) - offset, maxY = points.Max(p => p.Y) + offset;
            if (maxX - minX < 1e-6 || maxY - minY < 1e-6) throw new InvalidOperationException("Отступ создаёт пустую рамку обрезки.");
            // Z, глубину разреза и ориентацию не меняем.
            var box = new BoundingBoxXYZ
            {
                Enabled = true,
                Transform = original.Transform,
                Min = new XYZ(minX, minY, original.Min.Z),
                Max = new XYZ(maxX, maxY, original.Max.Z)
            };
            view.CropBoxActive = true;
            view.CropBox = box;
        }

        private static bool IsBreakLine(string name)
        {
            return new[] { "обрыв", "разрыв", "break" }.Any(s => name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IEnumerable<XYZ> CornersInView(BoundingBoxXYZ box, Transform viewTransform)
        {
            var inverse = viewTransform.Inverse;
            foreach (double x in new[] { box.Min.X, box.Max.X })
                foreach (double y in new[] { box.Min.Y, box.Max.Y })
                    foreach (double z in new[] { box.Min.Z, box.Max.Z })
                    {
                        var point = inverse.OfPoint(box.Transform.OfPoint(new XYZ(x, y, z)));
                        if (!double.IsNaN(point.X) && !double.IsInfinity(point.X) && !double.IsNaN(point.Y) && !double.IsInfinity(point.Y)) yield return point;
                    }
        }
    }
}
