using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace ARV.ElementViews
{
    internal static class SheetFormatParameters
    {
        private sealed class Target
        {
            internal string Name,Value;
            internal Parameter Parameter;
        }
        internal static void Apply(IList<Element> blocks,string a,string x)
        {
            var targets=new List<Target>();
            foreach(var target in new[]{new Target{Name="А",Value=a},new Target{Name="х",Value=x}})
            {
                if(string.IsNullOrWhiteSpace(target.Value))continue;
                target.Value=target.Value.Trim();
                var matches=blocks.SelectMany(block=>block.Parameters.Cast<Parameter>()).Where(p=>p.Definition!=null && p.Definition.Name==target.Name).ToList();
                if(matches.Count!=1)throw new InvalidOperationException("Параметр экземпляра основной надписи «"+target.Name+"» не найден однозначно.");
                target.Parameter=matches[0];Validate(target.Parameter,target.Name,target.Value);targets.Add(target);
            }
            foreach(var target in targets)Set(target.Parameter,target.Name,target.Value);
        }
        private static void Validate(Parameter parameter,string name,string value)
        {
            if(parameter.IsReadOnly)throw new InvalidOperationException("Параметр «"+name+"» основной надписи недоступен для записи (возможно, задан формулой).");
            if(parameter.StorageType==StorageType.Integer && !int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out _))throw new InvalidOperationException("Для параметра «"+name+"» введите целое число.");
            if(parameter.StorageType!=StorageType.Integer && parameter.StorageType!=StorageType.String && parameter.StorageType!=StorageType.Double)throw new InvalidOperationException("Неподдерживаемый тип параметра «"+name+"».");
        }
        private static void Set(Parameter parameter,string name,string value)
        {
            Validate(parameter,name,value);bool success;
            switch(parameter.StorageType)
            {
                case StorageType.String:
                    if(parameter.AsString()==value)return;
                    success=parameter.Set(value) && parameter.AsString()==value;break;
                case StorageType.Integer:
                    int number=int.Parse(value,CultureInfo.InvariantCulture);
                    if(parameter.AsInteger()==number)return;
                    success=parameter.Set(number) && parameter.AsInteger()==number;break;
                default:success=parameter.SetValueString(value);break;
            }
            if(!success)throw new InvalidOperationException("Не удалось записать параметр «"+name+"» основной надписи.");
        }
    }
}
