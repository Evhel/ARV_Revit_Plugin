using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.Common;
namespace ARV.ElementViews
{
    internal static class ScheduleCatalog
    {
        internal static void Fill(Document doc,Catalog catalog)
        {
            foreach(var schedule in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(s=>!s.IsTemplate&&!s.IsTitleblockRevisionSchedule&&!s.IsInternalKeynoteSchedule).OrderBy(s=>s.Name,Algorithms.NameComparer))
            {
                var info=new ScheduleInfo{Id=schedule.Id.IntegerValue,Name=schedule.Name,CanCopy=schedule.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)};
                var definition=schedule.Definition;
                for(int i=0;i<definition.GetFilterCount();i++)
                {
                    var filter=definition.GetFilter(i);var field=definition.GetField(filter.FieldId);
                    string kind=filter.IsStringValue?"String":filter.IsIntegerValue?"Integer":filter.IsDoubleValue?"Double":filter.IsElementIdValue?"ElementId":InferKind(doc,catalog,definition,field);
                    var choice=new ScheduleFilterChoice{Index=i,Name=field.GetName(),FieldName=field.GetName(),ValueKind=kind,OperatorKey=filter.FilterType.ToString()};
                    if(definition.CanFilterByParameterExistence(field.FieldId))Add(choice,"HasParameter","Параметр существует");
                    if(definition.CanFilterByValuePresence(field.FieldId)){Add(choice,"HasValue","Есть значение");Add(choice,"HasNoValue","Нет значения");}
                    if(kind!="None"&&definition.CanFilterByValue(field.FieldId))
                    {
                        Add(choice,"Equal","Равно");Add(choice,"NotEqual","Не равно");
                        if(kind!="ElementId"){Add(choice,"GreaterThan","Больше");Add(choice,"GreaterThanOrEqual","Больше или равно");Add(choice,"LessThan","Меньше");Add(choice,"LessThanOrEqual","Меньше или равно");}
                    }
                    if(kind=="String"&&definition.CanFilterBySubstring(field.FieldId))
                    {Add(choice,"Contains","Содержит");Add(choice,"NotContains","Не содержит");Add(choice,"BeginsWith","Начинается с");Add(choice,"NotBeginsWith","Не начинается с");Add(choice,"EndsWith","Заканчивается на");Add(choice,"NotEndsWith","Не заканчивается на");}
                    if(!choice.Operators.Any(o=>o.Key==choice.OperatorKey))choice.OperatorKey=choice.Operators.FirstOrDefault()?.Key;
                    info.Filters.Add(choice);
                }
                foreach(var group in info.Filters.GroupBy(f=>f.FieldName).Where(g=>g.Count()>1))foreach(var filter in group)filter.Name=filter.FieldName+" (фильтр "+(filter.Index+1)+")";
                info.Parameters.Add(new Choice{Id=-1,Key="",Name="Не заполнять"});
                info.Parameters.AddRange(schedule.Parameters.Cast<Parameter>().Where(p=>!p.IsReadOnly&&p.StorageType!=StorageType.None&&p.Definition!=null&&p.Id.IntegerValue!=(int)BuiltInParameter.VIEW_NAME)
                    .OrderBy(p=>p.Definition.Name,Algorithms.NameComparer).Select(p=>new Choice{Id=p.Id.IntegerValue,Name=p.Definition.Name,Key=p.StorageType.ToString()}));
                catalog.Schedules.Add(info);
            }
        }
        private static void Add(ScheduleFilterChoice c,string key,string name){c.Operators.Add(new Choice{Key=key,Name=name});}
        private static string InferKind(Document doc,Catalog catalog,ScheduleDefinition definition,ScheduleField field)
        {
            if(definition.CanFilterBySubstring(field.FieldId))return "String";
            foreach(var data in catalog.Elements)
            {
                var element=doc.GetElement(new ElementId(data.Id));
                foreach(var owner in new[]{element,element==null?null:doc.GetElement(element.GetTypeId())}.Where(e=>e!=null))
                {
                    var parameter=owner.Parameters.Cast<Parameter>().FirstOrDefault(p=>p.Id==field.ParameterId);
                    if(parameter!=null)return parameter.StorageType.ToString();
                }
            }
            var spec=field.GetSpecTypeId();
            if(spec.Equals(SpecTypeId.String.Text))return "String";
            if(spec.Equals(SpecTypeId.Int.Integer)||spec.Equals(SpecTypeId.Boolean.YesNo))return "Integer";
            if(spec.Equals(SpecTypeId.Number)||UnitUtils.IsMeasurableSpec(spec))return "Double";
            return "None";
        }
    }
}
