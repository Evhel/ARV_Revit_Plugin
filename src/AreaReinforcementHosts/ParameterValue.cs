using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ARV.AreaReinforcementHosts
{
    internal sealed class ParameterValue
    {
        internal int ParameterId;
        internal string Name;
        internal StorageType Storage;
        internal object Value;
        internal ElementId Global;

        internal static List<ParameterValue> Read(Element element)
        {
            var values = new List<ParameterValue>();
            foreach (Parameter p in element.Parameters)
            {
                if (!p.HasValue || p.StorageType == StorageType.None) continue;
                // These four comparison indicators are not layer configuration inputs.
                // Revit can expose IsReadOnly=false but still reject Set. Preserve and
                // verify the actual bar types, spacing, hooks and activity instead.
                if (IsLayerMatch(p.Id.IntegerValue)) continue;
                var global = p.GetAssociatedGlobalParameter();
                if (p.IsReadOnly && global == ElementId.InvalidElementId) continue;
                object value;
                switch (p.StorageType)
                {
                    case StorageType.Double: value = p.AsDouble(); break;
                    case StorageType.Integer: value = p.AsInteger(); break;
                    case StorageType.ElementId: value = p.AsElementId(); break;
                    default: value = p.AsString() ?? ""; break;
                }
                values.Add(new ParameterValue { ParameterId = p.Id.IntegerValue, Name = p.Definition.Name, Storage = p.StorageType, Value = value, Global = global });
            }
            return values;
        }

        private Parameter Get(Element element)
        {
            var p = ParameterId < 0 ? element.get_Parameter((BuiltInParameter)ParameterId) :
                element.Parameters.Cast<Parameter>().SingleOrDefault(value => value.Id.IntegerValue == ParameterId);
            if (p == null || p.StorageType != Storage)
                throw new InvalidOperationException("Не найден параметр «" + Name + "» нового армирования.");
            return p;
        }

        private bool Equal(Parameter p)
        {
            switch (Storage)
            {
                case StorageType.Double: return Math.Abs(p.AsDouble() - (double)Value) <= 1e-8;
                case StorageType.Integer: return p.AsInteger() == (int)Value;
                case StorageType.ElementId: return p.AsElementId() == (ElementId)Value;
                default: return (p.AsString() ?? "") == (string)Value;
            }
        }

        internal bool TryApply(Element element)
        {
            var p = Get(element);
            if (!Equal(p))
            {
                if (p.IsReadOnly) return false;

                switch (Storage)
                {
                    case StorageType.Double: p.Set((double)Value); break;
                    case StorageType.Integer: p.Set((int)Value); break;
                    case StorageType.ElementId: p.Set((ElementId)Value); break;
                    default: p.Set((string)Value); break;
                }
                // Set(false) is not a transaction failure: a dependent field may become
                // writable after layout/layer settings have been restored. Check its value.
                if (!Equal(p)) return false;
            }
            if (Global != ElementId.InvalidElementId && p.GetAssociatedGlobalParameter() != Global)
                p.AssociateWithGlobalParameter(Global);
            return true;
        }

        internal static void ApplyAll(IEnumerable<ParameterValue> values, Element element)
        {
            // Режим раскладки/активность слоя могут разблокировать остальные поля.
            var pending = values.ToList();
            while (pending.Count > 0)
            {
                int before = pending.Count;
                foreach (var value in pending.ToList())
                    if (value.TryApply(element)) pending.Remove(value);
                element.Document.Regenerate();
                pending.RemoveAll(value => value.Matches(element));
                if (pending.Count == before)
                    throw new InvalidOperationException("Revit не принял значение параметра «" + pending[0].Name + "» (ID " + pending[0].ParameterId + "). Исходное армирование сохранено.");
            }
        }

        private static bool IsLayerMatch(int parameterId)
        {
            return parameterId == (int)BuiltInParameter.REBAR_SYSTEM_TOP_MAJOR_MATCHES_TOP_MINOR ||
            parameterId == (int)BuiltInParameter.REBAR_SYSTEM_BOTTOM_MAJOR_MATCHES_BOTTOM_MINOR ||
            parameterId == (int)BuiltInParameter.REBAR_SYSTEM_TOP_MAJOR_MATCHES_BOTTOM_MAJOR ||
            parameterId == (int)BuiltInParameter.REBAR_SYSTEM_TOP_MINOR_MATCHES_BOTTOM_MINOR;
        }

        private bool Matches(Element element)
        {
            var p = Get(element);
            return Equal(p) && (Global == ElementId.InvalidElementId || p.GetAssociatedGlobalParameter() == Global);
        }

        internal void Verify(Element element)
        {
            var p = Get(element);
            if (!Equal(p) || (Global != ElementId.InvalidElementId && p.GetAssociatedGlobalParameter() != Global))
                throw new InvalidOperationException("После смены основы изменился параметр «" + Name + "».");
        }
    }

}
