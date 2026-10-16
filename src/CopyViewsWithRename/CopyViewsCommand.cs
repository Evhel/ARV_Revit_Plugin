using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace CopyViewsWithRename
{
    [Transaction(TransactionMode.Manual)]
    public class CopyViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                var views = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<View>()
                    .Where(v => !v.IsTemplate && !(v is ViewSheet)).OrderBy(v => v.Name, Algorithms.NameComparer).ToList();
                if (views.Count == 0)
                {
                    TaskDialog.Show("Копировать виды", "Выберите виды в диспетчере проекта.");
                    return Result.Cancelled;
                }
                var window = new CopyViewsWindow(views);
                new System.Windows.Interop.WindowInteropHelper(window) { Owner = data.Application.MainWindowHandle };
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                if (window.ShowDialog() != true) return Result.Cancelled;
                var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
                var errors = new List<string>();
                int created = 0;
                using (var transaction = new Transaction(doc, "Копировать виды"))
                {
                    transaction.Start();
                    foreach (var view in views)
                    {
                        if (!view.CanViewBeDuplicated(window.DuplicateOption))
                        {
                            errors.Add(view.Name + ": выбранный режим копирования недоступен.");
                            continue;
                        }
                        for (int i = 1; i <= window.CopiesCount; i++)
                        {
                            using (var item = new SubTransaction(doc))
                            {
                                item.Start();
                                try
                                {
                                    string requested = view.Name;
                                    if (window.UseReplace && !string.IsNullOrEmpty(window.FindText)) requested = requested.Replace(window.FindText, window.ReplaceText);
                                    requested += window.CopiesCount > 1 || !window.UseReplace ? "_копия" + i : "_" + i;
                                    var copy = (View)doc.GetElement(view.Duplicate(window.DuplicateOption));
                                    copy.Name = Algorithms.UniqueName(requested, names);
                                    if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Изменение не подтверждено.");
                                    created++;
                                }
                                catch (Exception ex)
                                {
                                    if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                                    errors.Add(view.Name + ": " + ex.Message);
                                }
                            }
                        }
                    }
                    if (created == 0) { transaction.RollBack(); TaskDialog.Show("Копировать виды", string.Join("\n", errors)); return Result.Cancelled; }
                    RevitSupport.Commit(transaction);
                }
                TaskDialog.Show("Копировать виды", $"Создано копий: {created}. Ошибок/пропусков: {errors.Count}.\n" + string.Join("\n", errors));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
