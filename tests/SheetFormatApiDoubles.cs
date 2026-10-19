// Limited doubles: no native Revit document, family formulas, units or transactions.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.ElementViews;
namespace Autodesk.Revit.DB
{
    public enum StorageType {None,String,Integer,Double,ElementId}
    public class ForgeTypeId
    {
        public string TypeId;
        public override bool Equals(object other)=>other is ForgeTypeId id && id.TypeId==TypeId;
        public override int GetHashCode()=>(TypeId??"").GetHashCode();
    }
    public static class SpecTypeId {public static readonly ForgeTypeId Number=new ForgeTypeId{TypeId="number"};}
    public class Definition {public string Name;public ForgeTypeId Spec=SpecTypeId.Number;public ForgeTypeId GetDataType()=>Spec;}
    public class Units {}
    public class Document {public Units GetUnits()=>new Units();public Element GetElement(ElementId id)=>id.IntegerValue==7?new Element():null;}
    public static class UnitFormatUtils
    {
        public static bool TryParse(Units units,ForgeTypeId spec,string text,out double value)
        {
            bool ok=double.TryParse(text.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value);
            if(spec.TypeId=="test-length-mm")value/=304.8;
            return ok;
        }
    }
    public class Parameter
    {
        public Definition Definition {get;set;}
        public StorageType StorageType {get;set;}
        public bool IsReadOnly {get;set;}
        public bool Refuse,IgnoreSet,FalseAfterSet;
        public Element Element {get;set;}
        public double Real;
        public string Text,ValueString;
        public int Number,SetCalls;
        public string AsString()=>Text;
        public int AsInteger()=>Number;
        public bool Set(string value){SetCalls++;if(Refuse)return false;if(!IgnoreSet)Text=value;return !FalseAfterSet;}
        public bool Set(int value){SetCalls++;if(Refuse)return false;if(!IgnoreSet)Number=value;return !FalseAfterSet;}
        public double AsDouble()=>Real;
        public bool Set(double value){SetCalls++;if(Refuse)return false;if(!IgnoreSet)Real=value;return !FalseAfterSet;}
        public bool SetValueString(string value){SetCalls++;ValueString=value;return false;}
    }
    public class Element {public readonly List<Parameter> Parameters=new List<Parameter>();public Document Document=new Document();}
}
public static class SheetFormatRegression
{
    private static Element With(string name,Parameter p){var e=new Element();p.Definition=new Definition{Name=name};p.Element=e;e.Parameters.Add(p);return e;}
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
        var real=new Parameter{StorageType=StorageType.Double};SheetFormatParameters.Apply(new[]{With("х",real)},null,"1,5");check(real.Real==1.5&&real.ValueString==null,"unitless family Number written with Set(double) even when SetValueString refuses");
        a=Int();a.Number=2;SheetFormatParameters.Apply(new[]{With("А",a)},"2",null);check(a.SetCalls==0,"same value avoids write");
        a=Int();instance=With("А",a);check(Throws(()=>SheetFormatParameters.Apply(new[]{instance},"2","3"))&&a.SetCalls==0,"both instance parameters validated before writing");
        var numeric=new Parameter{StorageType=StorageType.Double};SheetFormatParameters.Apply(new[]{With("А",numeric)},"2.5",null);check(numeric.Real==2.5,"decimal dot accepted for family Number");
        var dimensional=new Parameter{StorageType=StorageType.Double};var dimBlock=With("А",dimensional);dimensional.Definition.Spec=new ForgeTypeId{TypeId="test-length-mm"};SheetFormatParameters.Apply(new[]{dimBlock},"100",null);check(Math.Abs(dimensional.Real-100/304.8)<1e-12,"measured Double parsed in project units before typed write");
        var refusedButStored=new Parameter{StorageType=StorageType.Integer,FalseAfterSet=true};SheetFormatParameters.Apply(new[]{With("А",refusedButStored)},"2",null);check(refusedButStored.Number==2,"actual stored value wins over false Set return");
        var dependent=Int();dependent.Refuse=true;int regenerations=0;SheetFormatParameters.Apply(new[]{With("А",dependent)},"3",null,()=>{regenerations++;dependent.Number=3;});check(dependent.Number==3&&regenerations==1,"family regeneration checked before reporting refusal");
        var retry=Int();retry.Refuse=true;SheetFormatParameters.Apply(new[]{With("А",retry)},"4",null,()=>retry.Refuse=false);check(retry.Number==4&&retry.SetCalls==2,"one retry after dependencies regenerate");
        var invalidReal=new Parameter{StorageType=StorageType.Double};check(Throws(()=>SheetFormatParameters.Apply(new[]{With("А",invalidReal)},"NaN",null))&&invalidReal.SetCalls==0,"nonfinite family Number rejected before Set");
        var permanent=new Parameter{StorageType=StorageType.Double,Refuse=true};string reason="";try{SheetFormatParameters.Apply(new[]{With("А",permanent)},"2",null);}catch(InvalidOperationException ex){reason=ex.Message;}check(reason.Contains("Double")&&reason.Contains("2")&&reason.Contains("0"),"persistent refusal reports type requested and actual values");
    }
}
