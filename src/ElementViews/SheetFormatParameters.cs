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
            internal int Integer;
            internal double Real;
        }
        internal static void Apply(IList<Element> blocks,string a,string x,Action regenerate=null)
        {
            var targets=new List<Target>();
            foreach(var target in new[]{new Target{Name="А",Value=a},new Target{Name="х",Value=x}})
            {
                if(string.IsNullOrWhiteSpace(target.Value))continue;
                target.Value=target.Value.Trim();
                var matches=blocks.SelectMany(block=>block.Parameters.Cast<Parameter>()).Where(p=>p.Definition!=null && p.Definition.Name==target.Name).ToList();
                if(matches.Count!=1)throw new InvalidOperationException("Параметр экземпляра основной надписи «"+target.Name+"» не найден однозначно.");
                target.Parameter=matches[0];Prepare(target);targets.Add(target);
            }
            if(targets.Count==0)return;
            // Family dependencies can settle only after both fields have been written.
            for(int pass=0;pass<2;pass++)
            {
                foreach(var target in targets)
                {
                    if(Matches(target))continue;
                    switch(target.Parameter.StorageType)
                    {
                        case StorageType.String:target.Parameter.Set(target.Value);break;
                        case StorageType.Integer:target.Parameter.Set(target.Integer);break;
                        case StorageType.Double:target.Parameter.Set(target.Real);break;
                    }
                }
                regenerate?.Invoke();
                if(targets.All(Matches))return;
            }
            var failed=targets.First(t=>!Matches(t));
            var actualParameter=failed.Parameter;
            string actual=actualParameter.StorageType==StorageType.String?actualParameter.AsString():actualParameter.StorageType==StorageType.Integer?actualParameter.AsInteger().ToString(CultureInfo.InvariantCulture):actualParameter.AsDouble().ToString("R",CultureInfo.InvariantCulture);
            string dataType=actualParameter.StorageType==StorageType.Double?"; "+actualParameter.Definition.GetDataType().TypeId:"";
            throw new InvalidOperationException("Параметр «"+failed.Name+"» основной надписи не принял значение «"+failed.Value+"». Фактически: «"+actual+"». Тип: "+actualParameter.StorageType+dataType+".");
        }
        private static void Prepare(Target target)
        {
            var parameter=target.Parameter;
            if(parameter.IsReadOnly)throw new InvalidOperationException("Параметр «"+target.Name+"» основной надписи недоступен для записи (возможно, задан формулой).");
            switch(parameter.StorageType)
            {
                case StorageType.String:return;
                case StorageType.Integer:
                    if(!int.TryParse(target.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out target.Integer))throw new InvalidOperationException("Для параметра «"+target.Name+"» введите целое число.");
                    return;
                case StorageType.Double:
                    var spec=parameter.Definition.GetDataType();
                    bool parsed=spec.Equals(SpecTypeId.Number)
                        ?ARV.Common.Algorithms.TryNumber(target.Value,out target.Real)
                        :UnitFormatUtils.TryParse(parameter.Element.Document.GetUnits(),spec,target.Value,out target.Real);
                    if(!parsed || double.IsNaN(target.Real)||double.IsInfinity(target.Real))throw new InvalidOperationException("Значение «"+target.Value+"» не подходит параметру «"+target.Name+"» (Double; "+spec.TypeId+").");
                    return;
                default:throw new InvalidOperationException("Неподдерживаемый тип параметра «"+target.Name+"»: "+parameter.StorageType+".");
            }
        }
        private static bool Matches(Target target)
        {
            switch(target.Parameter.StorageType)
            {
                case StorageType.String:return target.Parameter.AsString()==target.Value;
                case StorageType.Integer:return target.Parameter.AsInteger()==target.Integer;
                case StorageType.Double:return Math.Abs(target.Parameter.AsDouble()-target.Real)<=1e-9*Math.Max(1,Math.Abs(target.Real));
                default:return false;
            }
        }
    }
}
