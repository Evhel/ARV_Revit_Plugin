using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace ARV.ElementViews
{
    internal static class SheetFormatParameters
    {
        internal static void Apply(Element sheet,IList<Element> blocks,string a,string x)
        {
            Set(sheet,blocks,"А",a);Set(sheet,blocks,"x",x);
        }
        private static void Set(Element sheet,IList<Element> blocks,string name,string value)
        {
            if(string.IsNullOrWhiteSpace(value))return;
            value=value.Trim();
            var candidates=sheet.GetParameters(name).ToList();
            if(candidates.Count==0)candidates=blocks.SelectMany(block=>block.GetParameters(name)).ToList();
            if(candidates.Count!=1)throw new InvalidOperationException("Параметр формата «"+name+"» не найден однозначно на листе или экземпляре основной надписи. Параметры типа не изменяются.");
            var parameter=candidates[0];
            if(parameter.IsReadOnly)throw new InvalidOperationException("Параметр формата «"+name+"» недоступен для записи.");
            bool success;
            switch(parameter.StorageType)
            {
                case StorageType.String:
                    if(parameter.AsString()==value)return;
                    success=parameter.Set(value) && parameter.AsString()==value;break;
                case StorageType.Integer:
                    if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int number))throw new InvalidOperationException("Для параметра «"+name+"» введите целое число.");
                    if(parameter.AsInteger()==number)return;
                    success=parameter.Set(number) && parameter.AsInteger()==number;break;
                case StorageType.Double:
                    success=parameter.SetValueString(value);break;
                default:throw new InvalidOperationException("Неподдерживаемый тип параметра формата «"+name+"».");
            }
            if(!success)throw new InvalidOperationException("Не удалось записать параметр формата «"+name+"».");
        }
    }
}
