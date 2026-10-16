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
        var empty=new Element();var none=new List<Element>();int copies=0;
        Func<Element> noCopy=()=>{copies++;return new Element();};
        SheetFormatParameters.Apply(none,empty,noCopy," ",null);check(copies==0,"blank values do not duplicate family type");
        var a=Int();var x=Int();var instance=With("А",a);x.Definition=new Definition{Name="х"};instance.Parameters.Add(x);
        SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"2","3");check(a.Number==2&&x.Number==3&&copies==0,"Cyrillic instance A and x both assigned without type copy");
        var latinA=Int();var latinX=Int();instance=With("A",latinA);latinX.Definition=new Definition{Name="x"};instance.Parameters.Add(latinX);
        SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"1","4");check(latinA.Number==1&&latinX.Number==4,"Latin aliases supported");
        var cyr=Int();instance=With("А",cyr);latinA=Int();latinA.Definition=new Definition{Name="A"};instance.Parameters.Add(latinA);
        SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"5",null);check(cyr.Number==5&&latinA.SetCalls==0,"Cyrillic name takes precedence over Latin alias");
        a=Int();instance=With(" А ",a);SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"6",null);check(a.Number==6,"outer whitespace in real definition names tolerated");
        var originalA=Int();var original=With("А",originalA);var copiedA=Int();var copy=With("А",copiedA);
        SheetFormatParameters.Apply(none,original,()=>{copies++;return copy;},"2",null);check(originalA.Number==0&&copiedA.Number==2&&copies==1,"type parameter written only to dedicated copy");
        var originalX=Int();originalX.Definition=new Definition{Name="х"};original.Parameters.Add(originalX);var copiedX=Int();copiedX.Definition=new Definition{Name="х"};copy.Parameters.Add(copiedX);int bothCopies=0;
        SheetFormatParameters.Apply(none,original,()=>{bothCopies++;return copy;},"3","4");check(bothCopies==1&&copiedA.Number==3&&copiedX.Number==4&&originalX.Number==0,"two type parameters share one copy and preserve source");
        var instanceA=Int();instance=With("А",instanceA);var rebuiltA=Int();
        SheetFormatParameters.Apply(new[]{instance},With("х",Int()),()=>{instance.Parameters.Clear();rebuiltA.Definition=new Definition{Name="А"};instance.Parameters.Add(rebuiltA);return With("х",Int());},"7","8");
        check(instanceA.SetCalls==0&&rebuiltA.Number==7,"mixed scope re-resolves instance after type replacement");
        check(Throws(()=>SheetFormatParameters.Apply(none,empty,noCopy,"1",null)),"missing family parameter reported");
        instance=With("А",Int());instance.Parameters.Add(new Parameter{Definition=new Definition{Name="А"},StorageType=StorageType.Integer});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"1",null)),"duplicate name rejected");
        instance=With("А",new Parameter{StorageType=StorageType.Integer,IsReadOnly=true});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"1",null)),"readonly or formula controlled parameter rejected");
        instance=With("А",Int());check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"A2",null)),"noninteger value rejected");
        instance=With("А",new Parameter{StorageType=StorageType.ElementId});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"1",null)),"unsupported storage rejected");
        instance=With("А",new Parameter{StorageType=StorageType.Integer,Refuse=true});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"1",null)),"Set refusal reported");
        instance=With("А",new Parameter{StorageType=StorageType.Integer,IgnoreSet=true});check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},empty,noCopy,"1",null)),"unchanged value after Set success rejected");
        var text=new Parameter{StorageType=StorageType.String};SheetFormatParameters.Apply(new[]{With("А",text)},empty,noCopy," 2 ",null);check(text.Text=="2","string stored and verified");
        var real=new Parameter{StorageType=StorageType.Double};SheetFormatParameters.Apply(new[]{With("х",real)},empty,noCopy,null,"1,5");check(real.ValueString=="1,5","native value-string path preserved for Double");
        a=Int();a.Number=2;SheetFormatParameters.Apply(new[]{With("А",a)},empty,noCopy,"2",null);check(a.SetCalls==0,"same value avoids unnecessary write");
    }
}
