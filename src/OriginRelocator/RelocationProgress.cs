using System;
using Autodesk.Revit.DB;
namespace Arv.OriginRelocator
{
    internal static class RelocationProgress
    {
        internal static bool Check(RelocationReport report, ElementId id, string stage, Action action)
        {
            try { action(); return true; }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { throw; }
            catch (Exception ex) { report.AddIssue(id, stage, ex.Message); return false; }
        }
        internal static bool TryElement(Document doc, RelocationReport report, ElementId id, string stage, Action action, bool recordFailure = true)
        {
            using (var item = new SubTransaction(doc))
            {
                item.Start();
                try
                {
                    action();
                    if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit не сохранил изменение элемента.");
                    return true;
                }
                catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { if(item.GetStatus()==TransactionStatus.Started)item.RollBack(); throw; }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { if(item.GetStatus()==TransactionStatus.Started)item.RollBack(); throw; }
                catch (Exception ex)
                {
                    if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                    bool committed = item.GetStatus() == TransactionStatus.Committed;
                    if (recordFailure || committed) report.AddIssue(id, stage, ex.Message);
                    return committed;
                }
            }
        }
    }
}
