using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;

namespace PlaceViewsOnSheet
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Получаем выбранные виды в диспетчере проекта
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                List<View> selectedViews = new List<View>();

                foreach (ElementId id in selectedIds)
                {
                    Element element = doc.GetElement(id);
                    if (element is View view && !(element is ViewSheet) && !view.IsTemplate)
                    {
                        selectedViews.Add(view);
                    }
                }

                if (selectedViews.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Пожалуйста, выберите виды в диспетчере проекта перед запуском команды.");
                    return Result.Failed;
                }

                // Получаем все листы
                List<ViewSheet> sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                if (sheets.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте нет ни одного листа.");
                    return Result.Failed;
                }

                // Открываем WPF окно
                MainWindow window = new MainWindow(selectedViews, sheets);

                // Устанавливаем владельца окна
                try
                {
                    IntPtr revitHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                    helper.Owner = revitHandle;
                }
                catch { }

                bool? result = window.ShowDialog();

                if (result == true && window.SelectedSheet != null && window.StepX > 0 && window.StepY > 0 && window.ViewsPerRow > 0)
                {
                    // Конвертируем миллиметры в футы (1 фут = 304.8 мм)
                    double stepXFeet = window.StepX / 304.8;
                    double stepYFeet = window.StepY / 304.8;

                    // Начальная позиция фиксированная (50 мм от края)
                    double startXFeet = 50 / 304.8;
                    double startYFeet = 50 / 304.8;

                    PlaceViewsOnSheet(doc, selectedViews, window.SelectedSheet, stepXFeet, stepYFeet, startXFeet, startYFeet, window.ViewsPerRow);
                    TaskDialog.Show("Успех", $"Размещено {selectedViews.Count} видов на листе.\n" +
                        $"Шаг: {window.StepX} мм по X, {window.StepY} мм по Y\n" +
                        $"Видов в ряду: {window.ViewsPerRow}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void PlaceViewsOnSheet(Document doc, List<View> views, ViewSheet sheet, double stepX, double stepY, double startX, double startY, int viewsPerRow)
        {
            using (Transaction trans = new Transaction(doc, "Размещение видов на листе"))
            {
                trans.Start();

                for (int i = 0; i < views.Count; i++)
                {
                    int col = i % viewsPerRow;
                    int row = i / viewsPerRow;

                    double x = startX + col * stepX;
                    double y = startY + row * stepY;

                    XYZ position = new XYZ(x, y, 0);

                    // Создаем видовое окно на листе
                    Viewport viewport = Viewport.Create(doc, sheet.Id, views[i].Id, position);
                }

                trans.Commit();
            }
        }
    }
}