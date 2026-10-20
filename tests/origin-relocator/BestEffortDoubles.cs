using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace Autodesk.Revit.Exceptions {
 public class RegenerationFailedException:Exception{} public class OperationCanceledException:Exception{}
}
namespace Autodesk.Revit.DB {
 public enum TransactionStatus {Uninitialized,Started,Committed,RolledBack}
 public class ElementId {
  public int IntegerValue;public ElementId(int id){IntegerValue=id;}public static readonly ElementId InvalidElementId=new ElementId(-1);
  public static bool operator ==(ElementId a,ElementId b){return Object.Equals(a,b);}public static bool operator !=(ElementId a,ElementId b){return !Object.Equals(a,b);}
  public override bool Equals(object other){return other is ElementId&&((ElementId)other).IntegerValue==IntegerValue;}public override int GetHashCode(){return IntegerValue;}
 }
 public class Element {public ElementId Id,OwnerViewId=ElementId.InvalidElementId;public string Name="item";public bool ViewSpecific {get{return OwnerViewId!=ElementId.InvalidElementId;}}public Element(int id){Id=new ElementId(id);} }
 public class View:Element {public ElementId Parent=ElementId.InvalidElementId;public View(int id):base(id){}public ElementId GetPrimaryViewId(){return Parent;} }
 public class ViewSheet:View {public string SheetNumber;public ViewSheet(int id,string number):base(id){SheetNumber=number;} }
 public class Viewport:Element {public ElementId SheetId,ViewId;public Viewport(int id,int sheet,int view):base(id){SheetId=new ElementId(sheet);ViewId=new ElementId(view);} }
 public class Document {
  public readonly Dictionary<int,Element> Elements=new Dictionary<int,Element>();public int Value,Rollbacks;public bool RejectNextCommit,ThrowOnGet,ThrowAfterCommit;
  public Element GetElement(ElementId id){if(ThrowOnGet)throw new Exception("read after commit failed");Element e;return Elements.TryGetValue(id.IntegerValue,out e)?e:null;}
 }
 public class FilteredElementCollector:IEnumerable {
  Document doc;Type type;public FilteredElementCollector(Document d){doc=d;}public FilteredElementCollector OfClass(Type t){type=t;return this;}
  public FilteredElementCollector WhereElementIsNotElementType(){return this;}
  public IEnumerator GetEnumerator(){return doc.Elements.Values.Where(e=>type==null||type.IsInstanceOfType(e)).GetEnumerator();}
 }
 public class SubTransaction:IDisposable {
  protected Document doc;protected int before;protected TransactionStatus status;
  public SubTransaction(Document d){doc=d;}public TransactionStatus Start(){before=doc.Value;return status=TransactionStatus.Started;}
  public virtual TransactionStatus Commit(){status=TransactionStatus.Committed;if(doc.ThrowAfterCommit){doc.ThrowAfterCommit=false;throw new Exception("exception after successful commit");}return status;}
  public TransactionStatus RollBack(){doc.Value=before;doc.Rollbacks++;return status=TransactionStatus.RolledBack;}
  public TransactionStatus GetStatus(){return status;}public void Dispose(){if(status==TransactionStatus.Started)RollBack();}
 }
 public class FailureHandlingOptions {public void SetFailuresPreprocessor(object p){}public void SetClearAfterRollback(bool b){} }
 public class Transaction:SubTransaction {
  public Transaction(Document d,string name):base(d){}
  public FailureHandlingOptions GetFailureHandlingOptions(){return new FailureHandlingOptions();}public void SetFailureHandlingOptions(FailureHandlingOptions o){}
  public override TransactionStatus Commit(){if(doc.RejectNextCommit){doc.RejectNextCommit=false;return RollBack();}return base.Commit();}
 }
}
namespace Arv.OriginRelocator {
 internal class RelocationPlan {public HashSet<int> OriginalIds=new HashSet<int>();public Dictionary<int,string> OriginalDescriptions=new Dictionary<int,string>();}
 internal sealed partial class RelocationReport {
  public List<string> Lines=new List<string>();public HashSet<int> InvalidDimensionDeletionRequests=new HashSet<int>(),DeletedInvalidDimensions=new HashSet<int>();
  public static string Describe(Element e){return e.Id.IntegerValue+" / "+e.GetType().Name+" / "+e.Name;}
 }
 internal class StrictFailures {public StrictFailures(RelocationReport r,RelocationPlan p){} }
 internal static class InvalidDimensionRecovery {public static void AcceptCommittedDeletions(Document d,RelocationPlan p,RelocationReport r){} }
 internal static class BestEffortTests {
  static int count;static void Check(bool b,string text){count++;if(!b)throw new Exception(text);}
  public static int Main(){
   var d=new Document();var p=new RelocationPlan();var r=new RelocationReport();
   d.Elements[100]=new ViewSheet(100,"КЖ-01");d.Elements[101]=new ViewSheet(101,"КЖ-05");d.Elements[10]=new View(10){Name="Основной разрез"};d.Elements[11]=new View(11){Parent=new ElementId(10)};
   d.Elements[201]=new Viewport(201,100,10);d.Elements[202]=new Viewport(202,101,11);d.Elements[5000]=new Element(5000){OwnerViewId=new ElementId(10)};d.Elements[5001]=new Element(5001){OwnerViewId=new ElementId(100)};d.Elements[7]=new Element(7);
   p.OriginalIds.UnionWith(d.Elements.Keys);r.CaptureContexts(d,p);
   r.AddIssue(new ElementId(5000),"Проверка оформления","расхождение 165000 мм");
   Check(r.Issues.Single().Contains("КЖ-01")&&r.Issues.Single().Contains("КЖ-05")&&r.Issues.Single().Contains("Основной разрез"),"primary annotation lists main and dependent sheet numbers and view name");
   r.AddIssue(new ElementId(5000),"Повторная проверка","расхождение 165000 мм");Check(r.Issues.Count==1,"repeated verification issue deduplicated across stages");
   r.AddIssue(new ElementId(5001),"Оформление листа","bad");Check(r.Issues.Last().Contains("лист № КЖ-01"),"sheet-owned element uses actual sheet number");
   r.AddIssue(new ElementId(202),"Видовой экран","bad");Check(r.Issues.Last().Contains("лист № КЖ-05"),"viewport error uses actual sheet number");
   r.AddIssue(new ElementId(7),"Модель","bad");Check(!r.Issues.Last().Contains("лист №"),"model error does not invent a sheet");
   d.Elements.Remove(5000);r.AddIssue(new ElementId(5000),"Удаление","missing");Check(r.Issues.Last().Contains("КЖ-05"),"deleted element retains cached sheet context");
   r.AddIssue(new ElementId(999),"Unknown","bad");Check(r.Issues.Last().Contains("ID: 999"),"uncached ID still reported");
   Check(r.IssueList().Contains("1. ")&&r.IssueList().Contains("КЖ-01"),"expanded issue list contains all contexts");
   Check(RelocationProgress.TryElement(d,r,new ElementId(7),"first",()=>d.Value=10)&&d.Value==10,"successful element committed");
   Check(!RelocationProgress.TryElement(d,r,new ElementId(7),"failed",()=>{d.Value=20;throw new InvalidOperationException("write refused");})&&d.Value==10,"bad element rollback preserves previous success");
   Check(RelocationProgress.TryElement(d,r,new ElementId(7),"next",()=>d.Value=30)&&d.Value==30,"next element processed after prior failure");
   Check(!RelocationProgress.Check(r,new ElementId(7),"verify",()=>{throw new InvalidOperationException("offset 165000");})&&d.Value==30,"verification discrepancy does not undo movement");
   int next=0;RelocationProgress.Check(r,null,"next verify",()=>next++);Check(next==1,"later verification continues");
   bool fatal=false;try{RelocationProgress.TryElement(d,r,null,"fatal",()=>{d.Value=40;throw new Autodesk.Revit.Exceptions.RegenerationFailedException();});}catch(Autodesk.Revit.Exceptions.RegenerationFailedException){fatal=true;}
   Check(fatal&&d.Value==30,"native regeneration failure rolled back then propagated to stage");
   bool cancelled=false;try{RelocationProgress.TryElement(d,r,null,"cancel",()=>{d.Value=40;throw new Autodesk.Revit.Exceptions.OperationCanceledException();});}catch(Autodesk.Revit.Exceptions.OperationCanceledException){cancelled=true;}
   Check(cancelled&&d.Value==30,"explicit cancellation not swallowed within element write");
   int issues=r.Issues.Count;RelocationProgress.TryElement(d,r,null,"batch",()=>{throw new Exception("batch retry");},false);Check(r.Issues.Count==issues,"failed batch can retry individually without spurious item issue");
   Check(RelocationEngine.Stage(d,"stage1",r,p,()=>d.Value=40)&&d.Value==40&&r.CommittedStages.Count==1,"stage checkpoint committed");
   Check(!RelocationEngine.Stage(d,"stage2",r,p,()=>{d.Value=50;throw new Exception("stage failed");})&&d.Value==40&&r.CommittedStages.Count==1,"failed later stage preserves prior committed stage");
   Check(RelocationEngine.Stage(d,"stage3",r,p,()=>d.Value=60)&&d.Value==60&&r.CommittedStages.Count==2,"following stage can complete");
   d.RejectNextCommit=true;r.InvalidDimensionDeletionRequests.Add(123);
   Check(!RelocationEngine.Stage(d,"native rejection",r,p,()=>{d.Value=70;r.InvalidDimensionDeletionRequests.Add(888);})&&d.Value==60&&r.InvalidDimensionDeletionRequests.SetEquals(new[]{123}),"native rejected stage rolls back only itself and clears uncommitted deletion request");
   Check(!RelocationEngine.Stage(d,"native regeneration",r,p,()=>{d.Value=80;throw new Autodesk.Revit.Exceptions.RegenerationFailedException();})&&d.Value==60,"native regeneration error cannot leave uncommitted stage");
   Check(RelocationEngine.Stage(d,"after regeneration",r,p,()=>d.Value=90)&&d.Value==90,"later transaction permitted after rollback checkpoint");
   d.Elements.Remove(7);int before=r.Issues.Count;Check(RelocationEngine.Stage(d,"missing original",r,p,()=>d.Value=100)&&d.Value==100&&r.Issues.Count==before+1,"missing original adds issue without rolling back successful transaction");
   Check(RelocationEngine.Stage(d,"post-commit read error",r,p,()=>{d.Value=110;d.ThrowOnGet=true;})&&d.Value==110,"post-commit diagnostic exception must not trigger a second relative move");d.ThrowOnGet=false;
   d.ThrowAfterCommit=true;Check(RelocationProgress.TryElement(d,r,null,"post-commit element error",()=>d.Value=120,false)&&d.Value==120,"committed element cannot be retried after post-commit exception");
   var extra=new Element(9999){OwnerViewId=new ElementId(100)};d.Elements.Add(9999,extra);r.CaptureContexts(d,p);r.AddIssue(extra.Id,"excluded annotation diagnostic","bad");Check(r.Issues.Last().Contains("КЖ-01"),"excluded annotation still receives sheet context without being moved");
   bool stop=false;try{RelocationEngine.Stage(d,"cancel stage",r,p,()=>{d.Value=130;throw new Autodesk.Revit.Exceptions.OperationCanceledException();});}catch(Autodesk.Revit.Exceptions.OperationCanceledException){stop=true;}Check(stop&&d.Value==120,"user cancellation stops later stages without discarding earlier commits");
   Console.WriteLine("PASS: "+count+" best-effort relocation and sheet-context checks; API doubles only, Revit not launched.");return 0;
  }
 }
}
