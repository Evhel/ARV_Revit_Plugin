// Limited filter objects: native Revit copy, template behavior and transaction boundaries are not simulated.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.ElementViews;
namespace Autodesk.Revit.DB
{
    public class ElementId
    {
        public int IntegerValue;public ElementId(int id){IntegerValue=id;}
        public override bool Equals(object value)=>value is ElementId id&&id.IntegerValue==IntegerValue;
        public override int GetHashCode()=>IntegerValue;
    }
    public class ScheduleFieldId {public int IntegerValue;public ScheduleFieldId(int n){IntegerValue=n;}}
    public enum ScheduleFilterType {HasParameter,HasValue,HasNoValue,Equal,NotEqual,GreaterThan,GreaterThanOrEqual,LessThan,LessThanOrEqual,Contains,NotContains,BeginsWith,NotBeginsWith,EndsWith,NotEndsWith}
    public class ScheduleFilter
    {
        public ScheduleFieldId FieldId;public ScheduleFilterType FilterType;private object value;
        public ScheduleFilter(ScheduleFieldId id,ScheduleFilterType type){FieldId=id;FilterType=type;}
        public ScheduleFilter(ScheduleFieldId id,ScheduleFilterType type,string v):this(id,type){value=v;}
        public ScheduleFilter(ScheduleFieldId id,ScheduleFilterType type,int v):this(id,type){value=v;}
        public ScheduleFilter(ScheduleFieldId id,ScheduleFilterType type,double v):this(id,type){value=v;}
        public ScheduleFilter(ScheduleFieldId id,ScheduleFilterType type,ElementId v):this(id,type){value=v;}
        public bool IsStringValue=>value is string;public bool IsIntegerValue=>value is int;public bool IsDoubleValue=>value is double;public bool IsElementIdValue=>value is ElementId;
        public string GetStringValue()=>(string)value;public int GetIntegerValue()=>(int)value;public double GetDoubleValue()=>(double)value;public ElementId GetElementIdValue()=>(ElementId)value;
    }
    public class ScheduleDefinition
    {
        public List<ScheduleFilter> Filters=new List<ScheduleFilter>();
        public int GetFilterCount()=>Filters.Count;public ScheduleFilter GetFilter(int i)=>Filters[i];public void SetFilter(int i,ScheduleFilter filter){Filters[i]=filter;}
    }
}
public static class ScheduleFilterRegression
{
    private static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    public static void Run(Action<bool,string> check)
    {
        var doc=new Document();var id=new ScheduleFieldId(3);var number=SpecTypeId.Number;
        var original=new ScheduleDefinition();original.Filters.Add(new ScheduleFilter(new ScheduleFieldId(1),ScheduleFilterType.Equal,"Пилон"));original.Filters.Add(new ScheduleFilter(new ScheduleFieldId(2),ScheduleFilterType.NotEqual,""));original.Filters.Add(new ScheduleFilter(id,ScheduleFilterType.Equal,"Пм-1"));
        var before=original.Filters.Select(ScheduleFilterTools.Signature).ToArray();
        var replacement=ScheduleFilterTools.BuildFilter(doc,id,number,"String",ScheduleFilterType.Equal,"Пм-13","S:Пм-13");original.SetFilter(2,replacement);ScheduleFilterTools.Verify(original,before,2,replacement);
        check(original.Filters.Count==3&&original.GetFilter(0).GetStringValue()=="Пилон"&&original.GetFilter(1).FilterType==ScheduleFilterType.NotEqual&&original.GetFilter(2).GetStringValue()=="Пм-13","only selected third schedule filter changed");
        original.SetFilter(0,new ScheduleFilter(new ScheduleFieldId(1),ScheduleFilterType.Equal,"unexpected"));check(Throws(()=>ScheduleFilterTools.Verify(original,before,2,replacement)),"unrequested filter change detected");
        var exists=ScheduleFilterTools.BuildFilter(doc,id,number,"None",ScheduleFilterType.HasParameter,"Пм-13",null);check(!exists.IsStringValue&&!exists.IsIntegerValue&&exists.FilterType==ScheduleFilterType.HasParameter,"parameter exists uses valueless native constructor");
        var hasValue=ScheduleFilterTools.BuildFilter(doc,id,number,"None",ScheduleFilterType.HasValue,"Пм-13",null);check(hasValue.FilterType==ScheduleFilterType.HasValue,"value presence does not parse mark as a number");
        var integer=ScheduleFilterTools.BuildFilter(doc,id,number,"Integer",ScheduleFilterType.GreaterThan,"12",null);check(integer.IsIntegerValue&&integer.GetIntegerValue()==12,"integer schedule comparisons remain typed");
        var real=ScheduleFilterTools.BuildFilter(doc,id,number,"Double",ScheduleFilterType.LessThanOrEqual,"1,5",null);check(real.IsDoubleValue&&real.GetDoubleValue()==1.5,"Double schedule comparisons accept decimal comma");
        var contains=ScheduleFilterTools.BuildFilter(doc,id,number,"String",ScheduleFilterType.Contains,"Пм-13",null);check(contains.FilterType==ScheduleFilterType.Contains&&contains.GetStringValue()=="Пм-13","substring condition and mark preserved");
        check(Throws(()=>ScheduleFilterTools.BuildFilter(doc,id,number,"Integer",ScheduleFilterType.Equal,"Пм-13",null)),"string mark rejected for integer target");
        check(Throws(()=>ScheduleFilterTools.BuildFilter(doc,id,number,"ElementId",ScheduleFilterType.Equal,"Пм-13","S:Пм-13")),"text mark not guessed as an element reference");
        var boolean=ScheduleFilterTools.BuildFilter(doc,id,number,"Integer",ScheduleFilterType.Equal,"Да","I:1");check(boolean.GetIntegerValue()==1,"raw integer supports localized yes/no display");
        var reference=ScheduleFilterTools.BuildFilter(doc,id,number,"ElementId",ScheduleFilterType.Equal,"Тип 1","E:7");check(reference.GetElementIdValue().IntegerValue==7,"source reference preserved without name guessing");
        check(Throws(()=>ScheduleFilterTools.BuildFilter(doc,id,number,"None",ScheduleFilterType.Equal,"Пм-1",null)),"unknown target type cannot silently become a text filter");
        var countChanged=new ScheduleDefinition();check(Throws(()=>ScheduleFilterTools.Verify(countChanged,before,-1,null)),"filter count change detected");
    }
}
