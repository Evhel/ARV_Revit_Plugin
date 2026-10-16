// Deliberately limited API doubles. No Revit document, units or transactions are simulated.
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ARV.ElementViews;
namespace Autodesk.Revit.DB
{
    public enum StorageType {None,String,Integer,Double,ElementId}
    public class Parameter
    {
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
    public class Element
    {
        public readonly Dictionary<string,IList<Parameter>> Parameters=new Dictionary<string,IList<Parameter>>();
        public IList<Parameter> GetParameters(string name)=>Parameters.ContainsKey(name)?Parameters[name]:new List<Parameter>();
    }
}
public static class SheetFormatRegression
{
    private static Element With(string name,Parameter p){var e=new Element();e.Parameters[name]=new List<Parameter>{p};return e;}
    private static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    public static void Run(Action<bool,string> check)
    {
        var sheet=new Element();var none=new List<Element>();
        SheetFormatParameters.Apply(sheet,none," ",null);check(sheet.Parameters.Count==0,"blank format inputs leave sheet untouched");
        var a=new Parameter{StorageType=StorageType.Integer};var x=new Parameter{StorageType=StorageType.Integer};sheet=With("А",a);sheet.Parameters["x"]=new List<Parameter>{x};
        SheetFormatParameters.Apply(sheet,none,"2","3");check(a.Number==2&&x.Number==3,"sheet A and x both assigned");
        var blockA=new Parameter{StorageType=StorageType.Integer};var blocks=new List<Element>{With("А",blockA)};
        SheetFormatParameters.Apply(new Element(),blocks,"1",null);check(blockA.Number==1,"format falls back to new titleblock instance");
        SheetFormatParameters.Apply(sheet,blocks,"4",null);check(a.Number==4&&blockA.Number==1,"sheet parameter takes priority without changing unrelated block parameter");
        check(Throws(()=>SheetFormatParameters.Apply(new Element(),none,"1",null)),"missing format parameter reported");
        sheet.Parameters["А"].Add(new Parameter{StorageType=StorageType.Integer});check(Throws(()=>SheetFormatParameters.Apply(sheet,none,"1",null)),"duplicate format names reported");
        sheet=With("А",new Parameter{StorageType=StorageType.Integer,IsReadOnly=true});check(Throws(()=>SheetFormatParameters.Apply(sheet,none,"1",null)),"readonly format rejected");
        sheet=With("А",new Parameter{StorageType=StorageType.Integer});check(Throws(()=>SheetFormatParameters.Apply(sheet,none,"A2",null)),"noninteger value rejected for integer format");
        sheet=With("А",new Parameter{StorageType=StorageType.ElementId});check(Throws(()=>SheetFormatParameters.Apply(sheet,none,"1",null)),"unsupported format storage rejected");
        sheet=With("А",new Parameter{StorageType=StorageType.Integer,Refuse=true});check(Throws(()=>SheetFormatParameters.Apply(sheet,none,"1",null)),"failed Set reported");
        sheet=With("А",new Parameter{StorageType=StorageType.Integer,IgnoreSet=true});check(Throws(()=>SheetFormatParameters.Apply(sheet,none,"1",null)),"successful Set without actual integer change rejected");
        var text=new Parameter{StorageType=StorageType.String};SheetFormatParameters.Apply(With("А",text),none," 2 ",null);check(text.Text=="2","string format value trimmed and verified");
        var real=new Parameter{StorageType=StorageType.Double};SheetFormatParameters.Apply(With("x",real),none,null,"1,5");check(real.ValueString=="1,5","double format delegated to Revit SetValueString for native units");
        var cyrillic=new Parameter{StorageType=StorageType.Integer};var latin=new Parameter{StorageType=StorageType.Integer,Number=9};
        var distinct=With("А",cyrillic);distinct.Parameters["A"]=new List<Parameter>{latin};
        SheetFormatParameters.Apply(distinct,none,"5",null);check(cyrillic.Number==5&&latin.Number==9,"Cyrillic A selected while Latin A remains unchanged");
        a=new Parameter{StorageType=StorageType.Integer,Number=2};SheetFormatParameters.Apply(With("А",a),none,"2",null);check(a.SetCalls==0,"unchanged integer does not require Set");
    }
}
