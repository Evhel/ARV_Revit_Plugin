// Limited doubles: no native Revit document, family formulas, units or transactions.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.ElementViews;
namespace Autodesk.Revit.DB
{
    public enum StorageType {None,String,Integer,Double,ElementId}
    public class Definition {public string Name;}
    public class Parameter
    {
        public Definition Definition {get;set;}
        public StorageType StorageType {get;set;}
        public bool IsReadOnly {get;set;}
        public bool Refuse,IgnoreSet;
        public string Text,ValueString;
        public int Number,SetCalls;
        public string AsString()=>Text;
        public int AsInteger()=>Number;
        public bool Set(string value){SetCalls++;if(Refuse)return false;if(!IgnoreSet)Text=value;return true;}
        public bool Set(int value){SetCalls++;if(Refuse)return false;if(!IgnoreSet)Number=value;return true;}
        public bool SetValueString(string value){SetCalls++;ValueString=value;return !Refuse;}
    }
    public class Element {public readonly List<Parameter> Parameters=new List<Parameter>();}
}
public static class SheetFormatRegression
{
    private static Element With(string name,Parameter p){var e=new Element();p.Definition=new Definition{Name=name};e.Parameters.Add(p);return e;}
    private static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    private static Parameter Int()=>new Parameter{StorageType=StorageType.Integer};
    public static void Run(Action<bool,string> check)
    {
        var none=new List<Element>();
        SheetFormatParameters.Apply(none," ",null);check(none.Count==0,"blank values leave instances untouched");
        var a=Int();var x=Int();var instance=With("А",a);x.Definition=new Definition{Name="х"};instance.Parameters.Add(x);
        SheetFormatParameters.Apply(new[]{instance},"2","3");check(a.Number==2&&x.Number==3,"Cyrillic instance A and x both assigned");
        var latinA=Int();var latinX=Int();instance=With("A",latinA);latinX.Definition=new Definition{Name="x"};instance.Parameters.Add(latinX);
        check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"1","4")),"Latin names cannot substitute confirmed Cyrillic names");
        var cyr=Int();instance=With("А",cyr);latinA=Int();latinA.Definition=new Definition{Name="A"};instance.Parameters.Add(latinA);
        SheetFormatParameters.Apply(new[]{instance},"5",null);check(cyr.Number==5&&latinA.SetCalls==0,"unrelated Latin A stays unchanged");
        check(Throws(()=>SheetFormatParameters.Apply(none,"1",null)),"missing instance parameter reported");
        instance=With("А",Int());instance.Parameters.Add(new Parameter{Definition=new Definition{Name="А"},StorageType=StorageType.Integer});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"1",null)),"duplicate instance name rejected");
        instance=With("А",new Parameter{StorageType=StorageType.Integer,IsReadOnly=true});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"1",null)),"readonly parameter rejected");
        instance=With("А",Int());check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"A2",null)),"noninteger value rejected");
        instance=With("А",new Parameter{StorageType=StorageType.ElementId});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"1",null)),"unsupported storage rejected");
        instance=With("А",new Parameter{StorageType=StorageType.Integer,Refuse=true});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"1",null)),"Set refusal reported");
        instance=With("А",new Parameter{StorageType=StorageType.Integer,IgnoreSet=true});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"1",null)),"actual value checked after Set");
        var text=new Parameter{StorageType=StorageType.String};SheetFormatParameters.Apply(new[]{With("А",text)}," 2 ",null);check(text.Text=="2","string trimmed and stored");
        var real=new Parameter{StorageType=StorageType.Double};SheetFormatParameters.Apply(new[]{With("х",real)},null,"1,5");check(real.ValueString=="1,5","native value-string path used for Double");
        a=Int();a.Number=2;SheetFormatParameters.Apply(new[]{With("А",a)},"2",null);check(a.SetCalls==0,"same value avoids write");
        a=Int();instance=With("А",a);check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"2","3"))&&a.SetCalls==0,"both instance parameters validated before writing");
    }
}
