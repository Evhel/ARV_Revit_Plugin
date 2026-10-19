using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Arv.OriginRelocator
{
    internal sealed class StrictFailures : IFailuresPreprocessor
    {
        private readonly RelocationReport report;
        private readonly RelocationPlan plan;
        private readonly HashSet<int> attempted = new HashSet<int>();
        public StrictFailures(RelocationReport report, RelocationPlan plan) { this.report = report; this.plan = plan; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            var invalid = new List<FailureMessageAccessor>();
            bool otherError = false;
            foreach (var failure in failures)
            {
                report.Lines.Add("Revit / " + failure.GetSeverity() + ": " + failure.GetDescriptionText() +
                    " / ID: " + string.Join(", ", failure.GetFailingElementIds().Select(x => x.IntegerValue)));
                if (failure.GetSeverity() == FailureSeverity.Warning) continue;
                if (failure.GetSeverity() == FailureSeverity.Error &&
                    failure.GetFailureDefinitionId().Equals(BuiltInFailures.DimensionFailures.DimensionReferencesInvalid)) invalid.Add(failure);
                else otherError = true;
            }
            // Resolve only the explicitly authorized error. All other errors still abort the operation.
            if (otherError) return FailureProcessingResult.ProceedWithRollBack;
            if (invalid.Count == 0) return FailureProcessingResult.Continue;
            var ids = invalid.SelectMany(f => f.GetFailingElementIds()).GroupBy(id => id.IntegerValue).Select(g => g.First()).ToList();
            var doc = accessor.GetDocument();
            if (ids.Count == 0 || ids.Any(id => attempted.Contains(id.IntegerValue) ||
                !plan.OriginalIds.Contains(id.IntegerValue) || !(doc.GetElement(id) is Dimension)))
            {
                report.Lines.Add("Удаление размеров не выполнено: ошибка повторилась либо её ID не являются размерами исходного набора.");
                return FailureProcessingResult.ProceedWithRollBack;
            }
            try
            {
                string reason;
                if (!accessor.IsElementsDeletionPermitted(ids, out reason))
                {
                    report.Lines.Add("Revit запретил удаление некорректных размеров: " + reason);
                    return FailureProcessingResult.ProceedWithRollBack;
                }
                attempted.UnionWith(ids.Select(id => id.IntegerValue));
                accessor.DeleteElements(ids);
                report.InvalidDimensionDeletionRequests.UnionWith(ids.Select(id => id.IntegerValue));
                report.Lines.Add("ЗАПРОШЕНО УДАЛЕНИЕ РАЗМЕРОВ С НЕКОРРЕКТНЫМИ ОПОРАМИ: " + string.Join(", ", ids.Select(id => id.IntegerValue)));
                // Regenerate failures and let Revit verify that deletion resolved the error.
                return FailureProcessingResult.ProceedWithCommit;
            }
            catch (Exception ex)
            {
                report.Lines.Add("Не удалось удалить некорректные размеры: " + ex.Message);
                return FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }
    internal static class InvalidDimensionRecovery
    {
        internal static void AcceptCommittedDeletions(Document doc, RelocationPlan plan, RelocationReport report)
        {
            // Called only after the stage committed. Do not exempt arbitrary missing elements from validation.
            foreach (int id in report.InvalidDimensionDeletionRequests.Where(id =>
                !report.DeletedInvalidDimensions.Contains(id) && doc.GetElement(new ElementId(id)) == null).ToList())
            {
                report.DeletedInvalidDimensions.Add(id);
                plan.Models.RemoveAll(s => s.Id.IntegerValue == id);
                plan.ModelRoots.RemoveAll(s => s.IntegerValue == id);
                plan.Annotations.RemoveAll(s => s.Id.IntegerValue == id);
                plan.Stationary.RemoveAll(s => s.Id.IntegerValue == id);
                plan.SheetStates.RemoveAll(s => s.Id.IntegerValue == id);
                plan.ViewGraphics.RemoveAll(g => g.State.Id.IntegerValue == id);
                plan.Pins.Remove(id);
                string description;
                if (!plan.OriginalDescriptions.TryGetValue(id, out description)) description = id.ToString();
                report.Lines.Add("УДАЛЁН НЕКОРРЕКТНЫЙ РАЗМЕР (этап подтверждён): " + description);
            }
        }
    }
}
