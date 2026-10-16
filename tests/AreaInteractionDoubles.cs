using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ARV.AreaReinforcementHosts;
namespace Autodesk.Revit.DB {
 public enum FailureSeverity{Warning,Error}
 public enum FailureProcessingResult{Continue}
 public sealed class FailureDefinitionId { readonly int value;public FailureDefinitionId(int id){value=id;}public override bool Equals(object x){var other=x as FailureDefinitionId;return other!=null&&other.value==value;}public override int GetHashCode(){return value;} }
 public static class BuiltInFailures {public static class RebarSystemFailures {public static readonly FailureDefinitionId AreaReinforcementOutSideOfHost=new FailureDefinitionId(1);} }
 public sealed class FailureMessageAccessor {public FailureSeverity Severity;public FailureDefinitionId Id;public FailureSeverity GetSeverity(){return Severity;}public FailureDefinitionId GetFailureDefinitionId(){return Id;} }
 public sealed class FailuresAccessor {public readonly List<FailureMessageAccessor> Messages=new List<FailureMessageAccessor>();public readonly List<FailureMessageAccessor> Deleted=new List<FailureMessageAccessor>(); public IList<FailureMessageAccessor> GetFailureMessages(){return Messages;}public void DeleteWarning(FailureMessageAccessor message){Deleted.Add(message);} }
 public interface IFailuresPreprocessor {FailureProcessingResult PreprocessFailures(FailuresAccessor accessor);}
 public class Element{} public sealed class Wall:Element{} public sealed class Reference{} public sealed class XYZ{}
 public sealed class Document {public Element Area;public Element GetElement(int id){return Area;} }
}
namespace Autodesk.Revit.DB.Structure {public sealed class AreaReinforcement:Element{}public sealed class RebarInSystem:Element{public int SystemId=1;}}
namespace Autodesk.Revit.UI.Selection {public interface ISelectionFilter {bool AllowElement(Element e);bool AllowReference(Reference r,XYZ point);}}
public static class AreaInteractionRegression {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static string Run(){
  var accessor=new FailuresAccessor();
  var outside=new FailureMessageAccessor{Severity=FailureSeverity.Warning,Id=new FailureDefinitionId(1)};
  var other=new FailureMessageAccessor{Severity=FailureSeverity.Warning,Id=new FailureDefinitionId(2)};
  var error=new FailureMessageAccessor{Severity=FailureSeverity.Error,Id=new FailureDefinitionId(1)};
  accessor.Messages.AddRange(new[]{outside,other,error});
  Check(new OutsideHostWarnings().PreprocessFailures(accessor)==FailureProcessingResult.Continue,"continue normal failure processing");
  Check(accessor.Deleted.Count==1&&accessor.Deleted[0]==outside,"delete only specified warning, preserve other warning and same-ID error");
  Check(new OutsideHostWarnings().PreprocessFailures(new FailuresAccessor())==FailureProcessingResult.Continue,"empty failure list");
  var doc=new Document{Area=new AreaReinforcement()};var filter=new AreaFilter(doc,false);
  Check(filter.AllowElement(doc.Area),"interactive selection accepts area reinforcement");
  Check(!filter.AllowElement(new RebarInSystem()),"interactive selection rejects individual system bars");
  Check(!filter.AllowElement(new Wall()),"interactive selection rejects walls");
  Check(!filter.AllowReference(new Reference(),new XYZ()),"reject subelement references");
  Check(new AreaFilter(doc).AllowElement(new RebarInSystem()),"single-command system bar selection preserved");
  return "PASS: "+count+" warning and selection-filter checks with API doubles; Revit not used.";
 }
}