using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ARV.AreaReinforcementHosts
{
    internal static class HostGeometry
    {
        internal const double Tolerance = 2.0 / 304.8;

        internal static IList<Wall> WallsOnSameBaseLevel(Document doc, Wall reference)
        {
            var parameter = reference.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            ElementId level = parameter == null ? ElementId.InvalidElementId : parameter.AsElementId();
            if (!(doc.GetElement(level) is Level)) throw new InvalidOperationException("У выбранной стены не определён базовый уровень.");
            return new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w => w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId() == level)
                .Where(w => RebarHostData.IsValidHost(w)).ToList();
        }

        internal static Element FindWall(Document doc, AreaSnapshot area, IEnumerable<Wall> candidates)
        {
            var lower = HostMatchingMath.LowerPointIndices(area.Samples.Select(p => p.Z).ToList(), Tolerance)
                .Select(i => area.Samples[i]).ToList();
            var matches = new List<int>();
            foreach (var wall in candidates)
            {
                // Проверяем ТОЛЬКО низ. Верх контура может находиться выше стены.
                var box = wall.get_BoundingBox(null);
                if (box == null || lower.Any(p => !InBox(box, p))) continue;
                var solids = GetSolids(wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToList();
                if (solids.Count == 0) continue;
                if (lower.All(p => IsInside(solids, p, area.Normal, area.Center))) matches.Add(wall.Id.IntegerValue);
            }
            return doc.GetElement(new ElementId(HostMatchingMath.UniqueHost(matches)));
        }

        private static bool InBox(BoundingBoxXYZ box, XYZ point)
        {
            var p = box.Transform.Inverse.OfPoint(point);
            return p.X >= box.Min.X - Tolerance && p.X <= box.Max.X + Tolerance &&
                p.Y >= box.Min.Y - Tolerance && p.Y <= box.Max.Y + Tolerance &&
                p.Z >= box.Min.Z - Tolerance && p.Z <= box.Max.Z + Tolerance;
        }

        private static IEnumerable<Solid> GetSolids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (GeometryObject item in geometry)
            {
                var solid = item as Solid;
                if (solid != null && solid.Faces.Size > 0 && solid.Volume > 1e-9) yield return solid;
                var instance = item as GeometryInstance;
                if (instance != null)
                    foreach (var nested in GetSolids(instance.GetInstanceGeometry())) yield return nested;
            }
        }

        private static bool IsInside(IList<Solid> solids, XYZ point, XYZ normal, XYZ center)
        {
            var inward = new XYZ(center.X - point.X, center.Y - point.Y, 0);
            if (inward.GetLength() > Tolerance) inward = inward.Normalize() * Tolerance;
            else inward = XYZ.Zero;
            // На границе объёма проверяем также точку на 2 мм внутрь и вверх.
            var probes = new[] { point, point + inward + XYZ.BasisZ * Tolerance };
            foreach (var probe in probes)
            {
                using (var ray = Line.CreateBound(probe - normal * 100, probe + normal * 100))
                using (var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside })
                    foreach (var solid in solids)
                    using (var hit = solid.IntersectWithCurve(ray, options))
                        for (int i = 0; i < hit.SegmentCount; i++)
                        using (var segment = hit.GetCurveSegment(i))
                        {
                            var projection = segment.Project(probe);
                            if (projection != null && projection.Distance <= Tolerance &&
                                HostMatchingMath.WithinSegment(probe.DistanceTo(segment.GetEndPoint(0)), probe.DistanceTo(segment.GetEndPoint(1)), segment.Length, Tolerance)) return true;
                        }
            }
            return false;
        }
    }
}
