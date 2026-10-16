using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace PlaceViewsOnSheet
{
    [Transaction(TransactionMode.Manual)]
    public class PlaceViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                // VIEW_NAME — встроенный параметр «Имя вида», независимо от языка Revit.
                var views = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<View>()
                    .Where(v => !v.IsTemplate && !(v is ViewSheet))
                    .OrderBy(v => RevitSupport.Text(v.get_Parameter(BuiltInParameter.VIEW_NAME)), Algorithms.NameComparer)
                    .ThenBy(v => v.Id.IntegerValue).ToList();
                if (views.Count == 0)
                {
                    TaskDialog.Show("Виды на лист", "Выберите виды в диспетчере проекта.");
                    return Result.Cancelled;
                }
                var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber, Algorithms.NameComparer).ToList();
                if (sheets.Count == 0)
                {
                    TaskDialog.Show("Виды на лист", "В проекте нет листов, доступных для размещения видов.");
                    return Result.Cancelled;
                }
                var window = new MainWindow(views, sheets);
                new System.Windows.Interop.WindowInteropHelper(window) { Owner = data.Application.MainWindowHandle };
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                if (window.ShowDialog() != true) return Result.Cancelled;
                var sheet = window.SelectedSheet;
                var errors = new List<string>();
                int placed = 0;
                using (var transaction = new Transaction(doc, "Виды на лист по имени"))
                {
                    transaction.Start();
                    var outline = sheet.Outline;
                    double startX = outline.Min.U + 50.0 / 304.8;
                    double startY = outline.Max.V - 50.0 / 304.8;
                    foreach (var view in views)
                    {
                        using (var item = new SubTransaction(doc))
                        {
                            item.Start();
                            try
                            {
                                int column = placed % window.ViewsPerRow;
                                int row = placed / window.ViewsPerRow;
                                var point = new XYZ(startX + column * window.StepX / 304.8,
                                    startY - row * window.StepY / 304.8, 0);
                                if (view is ViewSchedule schedule)
                                {
                                    if (schedule.IsTitleblockRevisionSchedule)
                                        throw new InvalidOperationException("Таблица изменений относится к основной надписи.");
                                    ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, point);
                                }
                                else
                                {
                                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                                        throw new InvalidOperationException("Вид уже размещён или не поддерживает размещение на этом листе.");
                                    Viewport.Create(doc, sheet.Id, view.Id, point);
                                }
                                doc.Regenerate();
                                if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Изменение не подтверждено.");
                                placed++;
                            }
                            catch (Exception ex)
                            {
                                if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                                errors.Add(view.Name + ": " + ex.Message);
                            }
                        }
                    }
                    if (placed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("Виды на лист", "Ни один вид не размещён.\n" + string.Join("\n", errors));
                        return Result.Cancelled;
                    }
                    RevitSupport.Commit(transaction);
                }
                TaskDialog.Show("Виды на лист", $"Размещено: {placed}. Пропущено: {errors.Count}.\n" +
                    "Порядок: по «Имени вида», слева направо и сверху вниз.\n" + string.Join("\n", errors));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
