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
            internal bool IsType;
        }
        internal static void Apply(IList<Element> blocks,Element symbol,Func<Element> copyType,string a,string x)
        {
            var targets=new List<Target>();
            foreach(var entry in new[]{new Target{Name="А",Value=a},new Target{Name="х",Value=x}})
            {
                if(string.IsNullOrWhiteSpace(entry.Value))continue;
                entry.Value=entry.Value.Trim();
                var instance=Find(blocks,entry.Name);
                entry.IsType=instance==null;
                var parameter=instance??Find(new[]{symbol},entry.Name);
                if(parameter==null)throw new InvalidOperationException("Не найден параметр «"+entry.Name+"» экземпляра или типа основной надписи. Сохраните отчёт кнопкой «Параметры семейства…».");
                Validate(parameter,entry.Name,entry.Value);targets.Add(entry);
            }
            // A new/reused dedicated type is assigned to the new titleblock before writing.
            // Resolve instance parameters again because changing the type can rebuild them.
            var targetType=targets.Any(t=>t.IsType)?copyType():symbol;
            foreach(var target in targets)
            {
                var parameter=Find(target.IsType?new[]{targetType}:blocks,target.Name);
                if(parameter==null)throw new InvalidOperationException("После смены типа потерян параметр «"+target.Name+"».");
                Set(parameter,target.Name,target.Value);
            }
        }
        private static Parameter Find(IEnumerable<Element> elements,string name)
        {
            var parameters=elements.Where(e=>e!=null).SelectMany(e=>e.Parameters.Cast<Parameter>()).Where(p=>p.Definition!=null).ToList();
            foreach(string alias in name=="А"?new[]{"А","A"}:new[]{"х","x"})
            {
                var matches=parameters.Where(p=>string.Equals(p.Definition.Name.Trim(),alias,StringComparison.OrdinalIgnoreCase)).ToList();
                if(matches.Count>1)throw new InvalidOperationException("Неоднозначный параметр «"+alias+"» основной надписи. Сохраните отчёт параметров семейства.");
                if(matches.Count==1)return matches[0];
            }
            return null;
        }
        private static void Validate(Parameter parameter,string name,string value)
        {
            if(parameter.IsReadOnly)throw new InvalidOperationException("Параметр «"+name+"» основной надписи недоступен для записи (возможно, задан формулой). Сохраните отчёт параметров семейства.");
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
