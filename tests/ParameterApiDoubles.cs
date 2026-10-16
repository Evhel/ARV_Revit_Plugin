using System;
using System.Collections.Generic;
using System.Linq;
using ARV.AreaReinforcementHosts;
namespace Autodesk.Revit.DB {
 public enum StorageType { None, Integer, Double, String, ElementId }
 public enum BuiltInParameter { REBAR_SYSTEM_TOP_MAJOR_MATCHES_TOP_MINOR=-1018020, REBAR_SYSTEM_BOTTOM_MAJOR_MATCHES_BOTTOM_MINOR=-1018021, REBAR_SYSTEM_TOP_MAJOR_MATCHES_BOTTOM_MAJOR=-1018022, REBAR_SYSTEM_TOP_MINOR_MATCHES_BOTTOM_MINOR=-1018023 }
 public sealed class ElementId { public int IntegerValue; public ElementId(int v){IntegerValue=v;} public static readonly ElementId InvalidElementId=new ElementId(-1); }
 public sealed class Definition { public string Name; }
 public sealed class Document { public Action OnRegenerate; public void Regenerate(){if(OnRegenerate!=null)OnRegenerate();} }
 public sealed class Element { public readonly List<Parameter> Parameters=new List<Parameter>(); public readonly Document Document=new Document(); public Parameter get_Parameter(BuiltInParameter id){return Parameters.SingleOrDefault(p=>p.Id.IntegerValue==(int)id);} }
 public sealed class Parameter {
  public ElementId Id; public Definition Definition; public StorageType StorageType=StorageType.Integer; public bool HasValue=true; public bool IsReadOnly; public object Value; public Func<object,bool> Setter; public ElementId Global=ElementId.InvalidElementId;
  public int AsInteger(){return (int)Value;} public double AsDouble(){return (double)Value;} public string AsString(){return (string)Value;} public ElementId AsElementId(){return (ElementId)Value;}
  private bool Put(object v){if(Setter!=null)return Setter(v);Value=v;return true;}
  public bool Set(int v){return Put(v);} public bool Set(double v){return Put(v);} public bool Set(string v){return Put(v);} public bool Set(ElementId v){return Put(v);}
  public ElementId GetAssociatedGlobalParameter(){return Global;} public void AssociateWithGlobalParameter(ElementId id){Global=id;}
 }
}
public static class ParameterRegression {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 static Autodesk.Revit.DB.Parameter Add(Autodesk.Revit.DB.Element e,int id,int value){var p=new Autodesk.Revit.DB.Parameter{Id=new Autodesk.Revit.DB.ElementId(id),Definition=new Autodesk.Revit.DB.Definition{Name="P"+id},Value=value};e.Parameters.Add(p);return p;}
 public static string Run(){
  // A matching display field refuses direct Set; restoring the actual layer data
  // recalculates it on regeneration. This reproduces the reported failure.
  foreach(int matchId in new[]{-1018020,-1018021,-1018022,-1018023}) foreach(int expected in new[]{0,1}) {
   var source=new Autodesk.Revit.DB.Element();Add(source,matchId,expected);Add(source,10,expected==0?120:200);
   var target=new Autodesk.Revit.DB.Element();var match=Add(target,matchId,1-expected);var spacing=Add(target,10,200);
   int refused=0;match.Setter=v=>{refused++;return false;};target.Document.OnRegenerate=()=>match.Value=spacing.AsInteger()==200?1:0;
   var snapshot=ParameterValue.Read(source);ParameterValue.ApplyAll(snapshot,target);foreach(var p in snapshot)p.Verify(target);
   Check(match.AsInteger()==expected,"matching layer restored "+matchId+"/"+expected);
  }
  {
   var source=new Autodesk.Revit.DB.Element();Add(source,1,10);Add(source,2,1);
   var target=new Autodesk.Revit.DB.Element();var dependent=Add(target,1,0);var controller=Add(target,2,0);
   dependent.Setter=v=>{if(controller.AsInteger()!=1)return false;dependent.Value=v;return true;};
   ParameterValue.ApplyAll(ParameterValue.Read(source),target);Check(dependent.AsInteger()==10,"deferred Set(false) retried after controller");
  }
  {
   var source=new Autodesk.Revit.DB.Element();Add(source,1,10);Add(source,2,1);
   var target=new Autodesk.Revit.DB.Element();var dependent=Add(target,1,0);var controller=Add(target,2,0);dependent.IsReadOnly=true;
   target.Document.OnRegenerate=()=>dependent.IsReadOnly=controller.AsInteger()!=1;
   ParameterValue.ApplyAll(ParameterValue.Read(source),target);Check(dependent.AsInteger()==10,"read-only field unlocked after regeneration");
  }
  {
   var source=new Autodesk.Revit.DB.Element();Add(source,1,10);var target=new Autodesk.Revit.DB.Element();var p=Add(target,1,0);p.Setter=v=>false;
   bool rejected=false;try{ParameterValue.ApplyAll(ParameterValue.Read(source),target);}catch(InvalidOperationException){rejected=true;}Check(rejected,"unrestored essential value must fail");
  }
  {
   var source=new Autodesk.Revit.DB.Element();Add(source,1,10);var target=new Autodesk.Revit.DB.Element();var p=Add(target,1,0);p.Setter=v=>{p.Value=v;return false;};
   ParameterValue.ApplyAll(ParameterValue.Read(source),target);Check(p.AsInteger()==10,"actual value wins over setter return flag");
  }
  return "PASS: "+count+" production parameter-transfer regression cases with API doubles; no Revit document.";
 }
}