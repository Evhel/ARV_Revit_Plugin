using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace WorksetViewsPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class WorksetViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                if (!doc.IsWorkshared)
                {
                    TaskDialog.Show("Рабочие наборы", "В проекте не включена совместная работа.");
                    return Result.Cancelled;
                }
                var worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets()
                    .OrderBy(w => w.Name, Algorithms.NameComparer).ToList();
                if (worksets.Count == 0) return Result.Cancelled;
                var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                if (type == null) throw new InvalidOperationException("В проекте нет типа 3D-вида.");
                var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
                var errors = new List<string>();
                int created = 0;
                using (var transaction = new Transaction(doc, "3D-виды по рабочим наборам"))
                {
                    transaction.Start();
                    foreach (var workset in worksets)
                    {
                        using (var item = new SubTransaction(doc))
                        {
                            item.Start();
                            try
                            {
                                var view = View3D.CreateIsometric(doc, type.Id);
                                view.ViewTemplateId = ElementId.InvalidElementId;
                                view.Name = Algorithms.UniqueName("РН_" + workset.Name, names);
                                foreach (Category category in doc.Settings.Categories)
                                    if (view.CanCategoryBeHidden(category.Id)) view.SetCategoryHidden(category.Id, false);
                                // Служебные наборы оставляем как есть; скрываем только остальные пользовательские.
                                foreach (var other in worksets)
                                    view.SetWorksetVisibility(other.Id, other.Id.IntegerValue == workset.Id.IntegerValue ? WorksetVisibility.Visible : WorksetVisibility.Hidden);
                                if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Изменение не подтверждено.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                                errors.Add(workset.Name + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0) { transaction.RollBack(); throw new InvalidOperationException(string.Join("\n", errors)); }
                    RevitSupport.Commit(transaction);
                }
                TaskDialog.Show("Рабочие наборы", $"Создано: {created}. Пропущено: {errors.Count}.\n" + string.Join("\n", errors));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
