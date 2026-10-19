using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace Autodesk.Revit.DB
{
    public enum FailureSeverity { Warning, Error, DocumentCorruption }
    public enum FailureProcessingResult { Continue, ProceedWithCommit, ProceedWithRollBack }
    public class ElementId { public int IntegerValue; public ElementId(int id){IntegerValue=id;} }
    public class Element { public ElementId Id; public Element(int id){Id=new ElementId(id);} }
    public class Dimension:Element { public Dimension(int id):base(id){} }
    public class Document
    {
        public readonly Dictionary<int,Element> Elements=new Dictionary<int,Element>();
        public Element GetElement(ElementId id){Element e;return Elements.TryGetValue(id.IntegerValue,out e)?e:null;}
    }
    public class FailureDefinitionId
    {
        public int Id;public FailureDefinitionId(int id){Id=id;}
        public override bool Equals(object other){return other is FailureDefinitionId&&((FailureDefinitionId)other).Id==Id;}
        public override int GetHashCode(){return Id;}
    }
    public static class BuiltInFailures { public static class DimensionFailures {public static readonly FailureDefinitionId DimensionReferencesInvalid=new FailureDefinitionId(1);} }
    public class FailureMessageAccessor
    {
        public FailureSeverity Severity;public FailureDefinitionId Definition;public List<ElementId> Ids=new List<ElementId>();
        public FailureSeverity GetSeverity(){return Severity;}public FailureDefinitionId GetFailureDefinitionId(){return Definition;}
        public IList<ElementId> GetFailingElementIds(){return Ids;}public string GetDescriptionText(){return "localized test message";}
    }
    public interface IFailuresPreprocessor {FailureProcessingResult PreprocessFailures(FailuresAccessor accessor);}
    public class FailuresAccessor
    {
        public Document Doc;public List<FailureMessageAccessor> Messages=new List<FailureMessageAccessor>();
        public bool Permit=true,ThrowOnDelete,KeepElements;public int DeleteCalls;public List<int> Deleted=new List<int>();
        public IList<FailureMessageAccessor> GetFailureMessages(){return Messages;}public Document GetDocument(){return Doc;}
        public bool IsElementsDeletionPermitted(IList<ElementId> ids,out string reason){reason=Permit?"":"denied";return Permit;}
        public void DeleteElements(IList<ElementId> ids){DeleteCalls++;if(ThrowOnDelete)throw new InvalidOperationException("delete refused");foreach(var id in ids){Deleted.Add(id.IntegerValue);if(!KeepElements)Doc.Elements.Remove(id.IntegerValue);}}
    }
}
namespace Arv.OriginRelocator
{
    internal class ElementState {public ElementId Id;public ElementState(int id){Id=new ElementId(id);} }
    internal class ViewGraphicState {public ElementState State;public ViewGraphicState(int id){State=new ElementState(id);} }
    internal class RelocationPlan
    {
        public HashSet<int> OriginalIds=new HashSet<int>();public Dictionary<int,string> OriginalDescriptions=new Dictionary<int,string>();
        public List<ElementState> Models=new List<ElementState>(),Annotations=new List<ElementState>(),Stationary=new List<ElementState>(),SheetStates=new List<ElementState>();
        public List<ElementId> ModelRoots=new List<ElementId>();public List<ViewGraphicState> ViewGraphics=new List<ViewGraphicState>();
        public Dictionary<int,bool> Pins=new Dictionary<int,bool>();
    }
    internal class RelocationReport
    {
        public List<string> Lines=new List<string>();public HashSet<int> InvalidDimensionDeletionRequests=new HashSet<int>(),DeletedInvalidDimensions=new HashSet<int>();
    }
    internal static class InvalidDimensionTests
    {
        static int count;
        static void Check(bool ok,string text){count++;if(!ok)throw new Exception(text);}
        static FailureMessageAccessor Failure(FailureSeverity severity,int definition,params int[] ids)
        {return new FailureMessageAccessor{Severity=severity,Definition=new FailureDefinitionId(definition),Ids=ids.Select(i=>new ElementId(i)).ToList()};}
        static void Setup(out Document doc,out RelocationPlan plan,out RelocationReport report)
        {
            doc=new Document();plan=new RelocationPlan();report=new RelocationReport();
            doc.Elements.Add(4230239,new Dimension(4230239));doc.Elements.Add(4230260,new Dimension(4230260));doc.Elements.Add(7,new Element(7));
            plan.OriginalIds.UnionWith(doc.Elements.Keys);
            foreach(var id in plan.OriginalIds){plan.OriginalDescriptions[id]="element "+id;plan.Annotations.Add(new ElementState(id));plan.Pins.Add(id,true);}
        }
        public static int Main()
        {
            Document doc;RelocationPlan plan;RelocationReport report;Setup(out doc,out plan,out report);
            var a=new FailuresAccessor{Doc=doc};var p=new StrictFailures(report,plan);
            Check(p.PreprocessFailures(a)==FailureProcessingResult.Continue,"empty failure list");
            a.Messages.Add(Failure(FailureSeverity.Warning,1,4230239));
            Check(p.PreprocessFailures(a)==FailureProcessingResult.Continue&&a.DeleteCalls==0,"warnings do not delete dimensions");
            a.Messages.Clear();a.Messages.Add(Failure(FailureSeverity.Error,2,4230239));
            Check(p.PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&a.DeleteCalls==0,"other errors remain strict regardless of dimension ID");
            a.Messages.Clear();a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));a.Messages.Add(Failure(FailureSeverity.Error,1,4230260,4230239));
            Check(p.PreprocessFailures(a)==FailureProcessingResult.ProceedWithCommit&&a.Deleted.OrderBy(i=>i).SequenceEqual(new[]{4230239,4230260}),"only both reported dimensions deleted; duplicate failure IDs collapsed");
            Check(report.DeletedInvalidDimensions.Count==0&&report.InvalidDimensionDeletionRequests.Count==2,"requests not reported as committed deletions");
            InvalidDimensionRecovery.AcceptCommittedDeletions(doc,plan,report);
            Check(report.DeletedInvalidDimensions.Count==2&&plan.Annotations.Single().Id.IntegerValue==7&&plan.Pins.Count==1&&plan.OriginalIds.Count==3,"committed dimensions pruned while protection of originals retained");
            RelocationEngine.VerifyOriginalElements(doc,plan,report);Check(true,"authorized missing dimensions pass original-element check");
            int lines=report.Lines.Count;InvalidDimensionRecovery.AcceptCommittedDeletions(doc,plan,report);Check(report.Lines.Count==lines,"committed removal reported once");
            doc.Elements.Remove(7);bool rejected=false;try{RelocationEngine.VerifyOriginalElements(doc,plan,report);}catch(InvalidOperationException){rejected=true;}Check(rejected,"collateral deletion of any other original still aborts relocation");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc};a.Messages.Add(Failure(FailureSeverity.Error,1,7));Check(new StrictFailures(report,plan).PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&a.DeleteCalls==0,"non-dimension failing ID not deleted");
            Setup(out doc,out plan,out report);plan.OriginalIds.Remove(4230239);a=new FailuresAccessor{Doc=doc};a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));Check(new StrictFailures(report,plan).PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&a.DeleteCalls==0,"excluded original-set element not deleted");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc,Permit=false};a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));Check(new StrictFailures(report,plan).PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&a.DeleteCalls==0,"native deletion permission respected");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc};a.Messages.Add(Failure(FailureSeverity.Error,1));Check(new StrictFailures(report,plan).PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack,"empty failing ID set not blindly resolved");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc,ThrowOnDelete=true};a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));Check(new StrictFailures(report,plan).PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&report.InvalidDimensionDeletionRequests.Count==0,"failed deletion not accepted");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc};a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));a.Messages.Add(Failure(FailureSeverity.Error,2,7));Check(new StrictFailures(report,plan).PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&a.DeleteCalls==0,"unrelated error prevents deletion and completion");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc,KeepElements=true};a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));p=new StrictFailures(report,plan);p.PreprocessFailures(a);Check(p.PreprocessFailures(a)==FailureProcessingResult.ProceedWithRollBack&&a.DeleteCalls==1,"unresolved repeat cannot create infinite commit loop");
            InvalidDimensionRecovery.AcceptCommittedDeletions(doc,plan,report);Check(report.DeletedInvalidDimensions.Count==0&&plan.Annotations.Count==3,"still-existing requested dimensions remain checked");
            Setup(out doc,out plan,out report);a=new FailuresAccessor{Doc=doc};a.Messages.Add(Failure(FailureSeverity.Error,1,4230239));p=new StrictFailures(report,plan);p.PreprocessFailures(a);a.Messages.Clear();a.Messages.Add(Failure(FailureSeverity.Error,1,4230260));Check(p.PreprocessFailures(a)==FailureProcessingResult.ProceedWithCommit&&a.DeleteCalls==2,"new failing dimension in later processing pass can be deleted");
            Setup(out doc,out plan,out report);plan.Models.Add(new ElementState(4230239));plan.ModelRoots.Add(new ElementId(4230239));plan.Stationary.Add(new ElementState(4230239));plan.SheetStates.Add(new ElementState(4230239));plan.ViewGraphics.Add(new ViewGraphicState(4230239));report.InvalidDimensionDeletionRequests.Add(4230239);doc.Elements.Remove(4230239);InvalidDimensionRecovery.AcceptCommittedDeletions(doc,plan,report);
            Check(plan.Models.Count==0&&plan.ModelRoots.Count==0&&plan.Stationary.Count==0&&plan.SheetStates.Count==0&&plan.ViewGraphics.Count==0&&plan.Annotations.Count==2,"all dimension snapshots pruned; unrelated snapshots retained");
            Console.WriteLine("PASS: "+count+" invalid-dimension recovery checks; isolated API doubles, Revit not launched.");return 0;
        }
    }
}
