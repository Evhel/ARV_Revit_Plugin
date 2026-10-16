using Autodesk.Revit.DB;

namespace ARV.AreaReinforcementHosts
{
    // Scoped to the rehosting transaction; all other warnings and all errors remain visible.
    internal sealed class OutsideHostWarnings : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning &&
                    failure.GetFailureDefinitionId().Equals(BuiltInFailures.RebarSystemFailures.AreaReinforcementOutSideOfHost))
                    accessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
