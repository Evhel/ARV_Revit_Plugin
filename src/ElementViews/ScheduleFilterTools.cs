using System;
using System.Globalization;
using Autodesk.Revit.DB;
using ARV.Common;
namespace ARV.ElementViews
{
    internal static class ScheduleFilterTools
    {
        internal static ScheduleFilter BuildFilter(Document doc,ScheduleFieldId id,ForgeTypeId spec,string kind,ScheduleFilterType type,string token,string raw)
        {
            if(type==ScheduleFilterType.HasParameter||type==ScheduleFilterType.HasValue||type==ScheduleFilterType.HasNoValue)return new ScheduleFilter(id,type);
            switch(kind)
            {
                case "String":return new ScheduleFilter(id,type,token);
                case "Integer":return new ScheduleFilter(id,type,Integer(token,raw));
                case "Double":return new ScheduleFilter(id,type,Real(doc,spec,token));
                case "ElementId":return new ScheduleFilter(id,type,Reference(doc,raw));
                default:throw new InvalidOperationException("Не определён тип значения выбранного фильтра. Условия наличия параметра доступны без значения.");
            }
        }
        internal static int Integer(string token,string raw)
        {
            if(int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value))return value;
            if(raw!=null&&raw.StartsWith("I:",StringComparison.Ordinal)&&int.TryParse(raw.Substring(2),out value))return value;
            throw new InvalidOperationException("Марка «"+token+"» не подходит числовому целому параметру.");
        }
        internal static double Real(Document doc,ForgeTypeId spec,string token)
        {
            double value;
            bool parsed=spec.Equals(SpecTypeId.Number)?Algorithms.TryNumber(token,out value):UnitFormatUtils.TryParse(doc.GetUnits(),spec,token,out value);
            if(!parsed||double.IsNaN(value)||double.IsInfinity(value))throw new InvalidOperationException("Значение «"+token+"» не подходит числовому параметру фильтра/вида.");
            return value;
        }
        internal static ElementId Reference(Document doc,string raw)
        {
            if(raw!=null&&raw.StartsWith("E:",StringComparison.Ordinal)&&int.TryParse(raw.Substring(2),out int value)&&doc.GetElement(new ElementId(value))!=null)return new ElementId(value);
            throw new InvalidOperationException("Целевой параметр хранит ссылку на элемент. Выбранный параметр марки не содержит совместимого ElementId.");
        }
        internal static string Signature(ScheduleFilter filter)
        {
            string value=filter.IsStringValue?"S:"+filter.GetStringValue():filter.IsIntegerValue?"I:"+filter.GetIntegerValue():filter.IsDoubleValue?"D:"+filter.GetDoubleValue().ToString("R",CultureInfo.InvariantCulture):filter.IsElementIdValue?"E:"+filter.GetElementIdValue().IntegerValue:"null";
            return filter.FieldId.IntegerValue+":"+filter.FilterType+":"+value;
        }
        internal static void Verify(ScheduleDefinition definition,string[] original,int changedIndex,ScheduleFilter replacement)
        {
            if(definition.GetFilterCount()!=original.Length)throw new InvalidOperationException("Поменялось число фильтров спецификации.");
            for(int i=0;i<original.Length;i++)
            {
                string expected=i==changedIndex&&replacement!=null?Signature(replacement):original[i];
                if(Signature(definition.GetFilter(i))!=expected)throw new InvalidOperationException("Revit не сохранил выбранный фильтр либо изменил другой. Проверьте управление фильтрами шаблоном.");
            }
        }
    }
}
