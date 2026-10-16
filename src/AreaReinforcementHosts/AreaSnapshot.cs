using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ARV.AreaReinforcementHosts
{
    internal sealed class BoundaryEdge
    {
        internal Curve Curve;
        internal XYZ OriginalStart;
        internal List<ParameterValue> Parameters;
    }

    internal sealed class LayerSnapshot
    {
        internal AreaReinforcementLayerType Type;
        internal bool Active;
        internal int Count;
        internal XYZ Direction;
        internal readonly Dictionary<int, XYZ> Moves = new Dictionary<int, XYZ>();
        internal readonly HashSet<int> Excluded = new HashSet<int>();
    }

    internal sealed class AreaSnapshot
    {
        internal readonly List<BoundaryEdge> Boundary = new List<BoundaryEdge>();
        internal readonly List<XYZ> Samples = new List<XYZ>();
        internal readonly List<LayerSnapshot> Layers = new List<LayerSnapshot>();
        internal List<ParameterValue> Parameters;
        internal XYZ Normal;
        internal XYZ Center;
        internal XYZ Direction;
        internal ElementId TypeId;
        internal ElementId BarType;
        internal bool Pinned;
        internal ElementId OptionId;
        internal bool HasRebar;

        internal AreaSnapshot(Document doc, AreaReinforcement source)
        {
            if (source.GroupId != ElementId.InvalidElementId || source.AssemblyInstanceId != ElementId.InvalidElementId)
                throw new InvalidOperationException("Армирование входит в группу или сборку: сначала исключите его из неё.");
            Parameters = ParameterValue.Read(source);
            Direction = source.Direction;
            TypeId = source.GetTypeId();
            Pinned = source.Pinned;
            OptionId = source.DesignOption == null ? ElementId.InvalidElementId : source.DesignOption.Id;
            foreach (var id in source.GetBoundaryCurveIds())
            {
                var edge = doc.GetElement(id) as AreaReinforcementCurve;
                if (edge == null || !edge.Curve.IsBound) throw new InvalidOperationException("Не удалось прочитать границу армирования.");
                Boundary.Add(new BoundaryEdge { Curve = edge.Curve.Clone(), OriginalStart = edge.Curve.GetEndPoint(0), Parameters = ParameterValue.Read(edge) });
            }
            OrderBoundary();
            foreach (var edge in Boundary)
            {
                Samples.AddRange(edge.Curve.Tessellate());
                // Для нижних прямых рёбер нужны точки между вершинами, не только их концы.
                for (int i = 0; i <= 4; i++) Samples.Add(edge.Curve.Evaluate(i / 4.0, true));
            }
            if (Samples.Count < 3) throw new InvalidOperationException("Контур армирования пуст.");
            Center = new XYZ(Samples.Average(p => p.X), Samples.Average(p => p.Y), Samples.Average(p => p.Z));
            var origin = Samples[0];
            var axis = Samples.FirstOrDefault(p => p.DistanceTo(origin) > 1e-6);
            if (axis == null) throw new InvalidOperationException("Контур вырожден.");
            var vector = axis - origin;
            var second = Samples.FirstOrDefault(p => vector.CrossProduct(p - origin).GetLength() > 1e-8);
            if (second == null) throw new InvalidOperationException("Контур не задаёт плоскость.");
            Normal = vector.CrossProduct(second - origin).Normalize();
            if (Samples.Any(p => Math.Abs((p - origin).DotProduct(Normal)) > 1e-5))
                throw new InvalidOperationException("Контур армирования не плоский.");
            BarType = Parameters.Where(p => p.Storage == StorageType.ElementId)
                .Select(p => (ElementId)p.Value).FirstOrDefault(id => doc.GetElement(id) is RebarBarType)
                ?? doc.GetDefaultElementTypeId(ElementTypeGroup.RebarBarType);
            if (!(doc.GetElement(BarType) is RebarBarType)) throw new InvalidOperationException("Не найден тип стержня для создания армирования.");
            HasRebar = ReinforcementSettings.GetReinforcementSettings(doc).HostStructuralRebar;
            foreach (AreaReinforcementLayerType layer in Enum.GetValues(typeof(AreaReinforcementLayerType)))
            {
                var state = new LayerSnapshot { Type = layer, Active = source.IsLayerActive(layer) };
                if (state.Active && HasRebar)
                {
                    state.Count = source.GetNumberOfLines(layer);
                    state.Direction = source.GetLayerDirection(layer);
                    for (int i = 0; i < state.Count; i++)
                    {
                        if (!source.IsLineIncluded(layer, i)) state.Excluded.Add(i);
                        var move = source.GetMovedLineTransform(layer, i);
                        if (move.Origin.GetLength() > 1e-8) state.Moves.Add(i, move.Origin);
                    }
                }
                Layers.Add(state);
            }
        }

        private void OrderBoundary()
        {
            if (Boundary.Count == 0) throw new InvalidOperationException("Нет кривых контура.");
            var remaining = Boundary.Skip(1).ToList();
            var ordered = new List<BoundaryEdge> { Boundary[0] };
            while (remaining.Count > 0)
            {
                XYZ end = ordered.Last().Curve.GetEndPoint(1);
                var next = remaining.FirstOrDefault(e => e.Curve.GetEndPoint(0).DistanceTo(end) < 1e-6 || e.Curve.GetEndPoint(1).DistanceTo(end) < 1e-6);
                if (next == null) throw new InvalidOperationException("Контур содержит несколько петель или разрывы. Армирование сохранено.");
                remaining.Remove(next);
                if (next.Curve.GetEndPoint(0).DistanceTo(end) >= 1e-6) next.Curve = next.Curve.CreateReversed();
                ordered.Add(next);
            }
            if (ordered.Last().Curve.GetEndPoint(1).DistanceTo(ordered[0].Curve.GetEndPoint(0)) >= 1e-6)
                throw new InvalidOperationException("Контур не замкнут.");
            Boundary.Clear(); Boundary.AddRange(ordered);
        }

        internal void Apply(Document doc, AreaReinforcement target)
        {
            foreach (var layer in Layers) target.SetLayerActive(layer.Active, layer.Type);
            doc.Regenerate();
            ParameterValue.ApplyAll(Parameters, target);
            doc.Regenerate();
            var edges = target.GetBoundaryCurveIds().Select(id => (AreaReinforcementCurve)doc.GetElement(id)).ToList();
            if (edges.Count != Boundary.Count) throw new InvalidOperationException("Изменилось число рёбер контура.");
            var unused = new List<AreaReinforcementCurve>(edges);
            foreach (var saved in Boundary)
            {
                var match = unused.FirstOrDefault(e => SameCurve(saved.Curve, e.Curve));
                if (match == null) throw new InvalidOperationException("Revit изменил положение или форму контура. Исходное армирование сохранено.");
                if (saved.Parameters.Count > 0 && match.Curve.GetEndPoint(0).DistanceTo(saved.OriginalStart) > 1e-5)
                    throw new InvalidOperationException("Revit развернул ребро контура: невозможно надёжно сохранить настройки крюков и сторон. Исходная зона сохранена.");
                ParameterValue.ApplyAll(saved.Parameters, match);
                foreach (var p in saved.Parameters) p.Verify(match);
                unused.Remove(match);
            }
            doc.Regenerate();
            foreach (var layer in Layers.Where(l => l.Active && HasRebar))
            {
                int count = target.GetNumberOfLines(layer.Type);
                if (count != layer.Count && (layer.Moves.Count > 0 || layer.Excluded.Count > 0))
                    throw new InvalidOperationException("Новая основа изменила число линий: невозможно надёжно перенести ручные смещения/исключения.");
                if ((layer.Moves.Count > 0 || layer.Excluded.Count > 0) &&
                    target.GetLayerDirection(layer.Type).Normalize().DotProduct(layer.Direction.Normalize()) < 1 - 1e-8)
                    throw new InvalidOperationException("Изменилось направление раскладки: ручные правки линий нельзя надёжно перенести.");
                foreach (var move in layer.Moves) target.MoveLine(move.Value, layer.Type, move.Key);
                foreach (var index in layer.Excluded) target.SetLineIncluded(false, layer.Type, index);
            }
        }

        internal void Verify(AreaReinforcement target)
        {
            foreach (var p in Parameters) p.Verify(target);
            var currentEdges = target.GetBoundaryCurveIds().Select(id => (AreaReinforcementCurve)target.Document.GetElement(id)).ToList();
            if (currentEdges.Count != Boundary.Count) throw new InvalidOperationException("Изменилось число рёбер контура.");
            foreach (var saved in Boundary)
            {
                var match = currentEdges.FirstOrDefault(e => SameCurve(saved.Curve, e.Curve));
                if (match == null) throw new InvalidOperationException("Не сохранился исходный контур армирования.");
                foreach (var p in saved.Parameters) p.Verify(match);
                currentEdges.Remove(match);
            }
            if (target.Direction.Normalize().DotProduct(Direction.Normalize()) < 1 - 1e-8)
                throw new InvalidOperationException("Изменилось главное направление армирования.");
            foreach (var layer in Layers)
            {
                if (target.IsLayerActive(layer.Type) != layer.Active) throw new InvalidOperationException("Изменился набор активных слоёв.");
                if (!layer.Active || !HasRebar) continue;
                foreach (var move in layer.Moves)
                    if (target.GetMovedLineTransform(layer.Type, move.Key).Origin.DistanceTo(move.Value) > 1e-6)
                        throw new InvalidOperationException("Не сохранилось ручное смещение линии.");
                foreach (var i in layer.Excluded)
                    if (target.IsLineIncluded(layer.Type, i)) throw new InvalidOperationException("Не сохранилось исключение линии.");
            }
        }

        private static bool SameCurve(Curve a, Curve b)
        {
            if (a.GetType() != b.GetType() || Math.Abs(a.Length - b.Length) > 1e-5) return false;
            bool forward = a.GetEndPoint(0).DistanceTo(b.GetEndPoint(0)) < 1e-5 && a.GetEndPoint(1).DistanceTo(b.GetEndPoint(1)) < 1e-5;
            bool reverse = a.GetEndPoint(0).DistanceTo(b.GetEndPoint(1)) < 1e-5 && a.GetEndPoint(1).DistanceTo(b.GetEndPoint(0)) < 1e-5;
            return (forward || reverse) && a.Evaluate(0.5, true).DistanceTo(b.Evaluate(0.5, true)) < 1e-5;
        }
    }
}
