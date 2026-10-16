using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Arv.OriginRelocator
{
    internal sealed class ElementState
    {
        public readonly ElementId Id;
        public readonly ElementId ViewId;
        public readonly XYZ[] Points;
        public readonly bool Annotation;
        private readonly bool boundaryPointSet;
        public readonly List<PointHandle> Handles = new List<PointHandle>();
        private ElementState(Element e, View view, bool annotation)
        {
            Id = e.Id; ViewId = view == null ? ElementId.InvalidElementId : view.Id;
            Annotation = annotation; boundaryPointSet = e is FilledRegion;
            Points = ReadPoints(e, view, annotation);
            if (annotation) CaptureHandles(e);
        }
        public static ElementState Capture(Element e, View view = null, bool annotation = false)
        {
            ElementState s = new ElementState(e, view, annotation);
            return s.Points.Length == 0 ? null : s;
        }
        public XYZ[] Current(Document doc)
        {
            Element e = doc.GetElement(Id);
            if (e == null) throw new InvalidOperationException("Исчез элемент " + Id.IntegerValue);
            return ReadPoints(e, doc.GetElement(ViewId) as View, Annotation);
        }
        private static XYZ[] ReadPoints(Element e, View view, bool annotation)
        {
            FilledRegion region = e as FilledRegion;
            if (region != null)
            {
                // BoundingBox can include display/control extents. Use actual sketch boundaries instead.
                List<XYZ> points = new List<XYZ>();
                foreach (CurveLoop loop in region.GetBoundaries())
                    foreach (Curve boundaryCurve in loop) points.AddRange(CurvePoints(boundaryCurve));
                return points.ToArray();
            }
            if (annotation)
            {
                // View translation moves a detail group's graphics but not its API LocationPoint.
                // Use displayed world-space geometry for both residuals and verification.
                Group detailGroup = e as Group;
                if (detailGroup != null) return GroupDisplayPoints(detailGroup, view);
                TextNote text = e as TextNote;
                if (text != null) return new[] { text.Coord };
                IndependentTag tag = e as IndependentTag;
                if (tag != null) return new[] { tag.TagHeadPosition };
                SpatialElementTag spatial = e as SpatialElementTag;
                if (spatial != null) return new[] { spatial.TagHeadPosition };
                Dimension dim = e as Dimension;
                if (dim != null)
                {
                    if (dim.NumberOfSegments > 0)
                        return dim.Segments.Cast<DimensionSegment>().Select(s => s.Origin).ToArray();
                    return new[] { dim.Origin };
                }
            }
            ReferencePlane reference = e as ReferencePlane;
            if (reference != null) return new[] { reference.BubbleEnd, reference.FreeEnd };
            Grid grid = e as Grid;
            if (grid != null) return CurvePoints(grid.Curve);
            AssemblyInstance assembly = e as AssemblyInstance;
            if (assembly != null) return new[] { assembly.GetTransform().Origin };
            LocationPoint point = e.Location as LocationPoint;
            if (point != null) return new[] { point.Point };
            LocationCurve curve = e.Location as LocationCurve;
            if (curve != null) return CurvePoints(curve.Curve);
            BoundingBoxXYZ box = e.get_BoundingBox(view);
            if (box == null) return new XYZ[0];
            // Two opposite corners check translation and unchanged extents for elements without a Location.
            return new[] { box.Transform.OfPoint(box.Min), box.Transform.OfPoint(box.Max) };
        }
        internal static XYZ[] GroupDisplayPoints(Group group, View view)
        {
            BoundingBoxXYZ box = group.get_BoundingBox(view) ?? group.get_BoundingBox(null);
            return box == null ? new XYZ[0] :
                new[] { box.Transform.OfPoint(box.Min), box.Transform.OfPoint(box.Max) };
        }
        private static XYZ[] CurvePoints(Curve curve)
        {
            if (curve == null) return new XYZ[0];
            if (curve.IsBound) return new[] { curve.GetEndPoint(0), curve.GetEndPoint(1), curve.Evaluate(0.5, true) };
            Line line = curve as Line;
            return line == null ? new[] { curve.Evaluate(0, false) } : new[] { line.Origin };
        }
        public XYZ Residual(Document doc, XYZ delta, bool project)
        {
            XYZ[] current = Current(doc);
            if (current.Length != Points.Length) throw new InvalidOperationException("Изменилась геометрия элемента " + Id.IntegerValue);
            XYZ result = boundaryPointSet ? PointSetCenter(Points) + delta - PointSetCenter(current) :
                Points[0] + delta - current[0];
            if (project) result = Project(result, (doc.GetElement(ViewId) as View).ViewDirection);
            return result;
        }
        public void Verify(Document doc, XYZ delta, bool project = false, bool verifyHandles = true)
        {
            XYZ[] current = Current(doc);
            if (current.Length != Points.Length)
                throw new InvalidOperationException("Изменилась геометрия элемента " + Id.IntegerValue);
            View view = doc.GetElement(ViewId) as View;
            if (boundaryPointSet)
            {
                XYZ normal = project && view != null ? Normal(view) : null;
                if (!TranslatedPointSetsMatch(Points, current, delta, normal, RelocationEngine.Tolerance))
                {
                    XYZ error = PointSetCenter(Points) + delta - PointSetCenter(current);
                    if (normal != null) error = Project(error, normal);
                    Element region = doc.GetElement(Id);
                    throw new InvalidOperationException("Не подтверждён перенос контуров " + RelocationReport.Describe(region) +
                        "; расхождение центра " + (error.GetLength() * 304.8).ToString("F3") + " мм" +
                        "; контрольных точек " + Points.Length + "; вид " + ViewId.IntegerValue +
                        "; группа " + region.GroupId.IntegerValue + ".");
                }
                return;
            }
            for (int i = 0; i < current.Length; ++i)
            {
                if ((!Annotation || !project) && Math.Abs(current[i].Z - Points[i].Z) > RelocationEngine.Tolerance)
                    throw new InvalidOperationException("Изменилась высота элемента " + Id.IntegerValue + ".");
                XYZ error = Points[i] + delta - current[i];
                if (project && view != null) error = Project(error, Normal(view));
                if (error.GetLength() > RelocationEngine.Tolerance)
                    throw new InvalidOperationException("Не подтверждён перенос " + RelocationReport.Describe(doc.GetElement(Id)) +
                        "; расхождение " + (error.GetLength() * 304.8).ToString("F3") + " мм.");
            }
            // Text/leader restoration happens after the movement phase. Final checks include all handles.
            if (verifyHandles)
                foreach (PointHandle handle in Handles)
                    handle.Verify(doc.GetElement(Id), delta, view);
        }
        internal static XYZ PointSetCenter(XYZ[] points)
        {
            XYZ sum = XYZ.Zero;
            foreach (XYZ point in points) sum += point;
            return sum.Multiply(1.0 / points.Length);
        }

        internal static bool TranslatedPointSetsMatch(XYZ[] before, XYZ[] current, XYZ delta,
            XYZ normal, double tolerance)
        {
            if (before.Length != current.Length) return false;
            bool[] used = new bool[current.Length];
            foreach (XYZ point in before)
            {
                bool found = false;
                XYZ expected = point + delta;
                for (int i = 0; i < current.Length; ++i)
                {
                    if (used[i]) continue;
                    XYZ error = expected - current[i];
                    if (normal != null) error = Project(error, normal);
                    if (error.GetLength() > tolerance) continue;
                    used[i] = true; found = true; break;
                }
                if (!found) return false;
            }
            return true;
        }


        public static XYZ Normal(View view) { return view is ViewSheet ? XYZ.BasisZ : view.ViewDirection; }
        public static XYZ Project(XYZ value, XYZ normal)
        { return value - normal.Multiply(value.DotProduct(normal)); }

        private void AddHandle(Element e, string name, Func<Element, XYZ> read, Action<Element, XYZ> write)
        {
            XYZ p = read(e);
            if (p != null) Handles.Add(new PointHandle(name, p, read, write));
        }
        private void CaptureHandles(Element e)
        {
            Dimension dim = e as Dimension;
            if (dim != null)
            {
                if (dim.NumberOfSegments == 0 && dim.IsTextPositionAdjustable())
                    AddHandle(e, "Текст размера", x => ((Dimension)x).TextPosition, (x,p) => ((Dimension)x).TextPosition = p);
                for (int i = 0; i < dim.NumberOfSegments; ++i)
                {
                    int index = i;
                    DimensionSegment segment = dim.Segments.get_Item(index);
                    if (segment.IsTextPositionAdjustable())
                        AddHandle(e, "Текст цепочки " + index, x => ((Dimension)x).Segments.get_Item(index).TextPosition,
                            (x,p) => ((Dimension)x).Segments.get_Item(index).TextPosition = p);
                }
            }
            IndependentTag tag = e as IndependentTag;
            if (tag != null && tag.HasLeader)
                foreach (Reference reference in tag.GetTaggedReferences())
                {
                    Reference r = reference;
                    if (tag.HasLeaderElbow(r))
                        AddHandle(e, "Колено марки", x => ((IndependentTag)x).GetLeaderElbow(r),
                            (x,p) => ((IndependentTag)x).SetLeaderElbow(r,p));
                    if (tag.LeaderEndCondition == LeaderEndCondition.Free)
                        AddHandle(e, "Свободный конец марки", x => ((IndependentTag)x).GetLeaderEnd(r),
                            (x,p) => ((IndependentTag)x).SetLeaderEnd(r,p));
                }
            SpatialElementTag spatial = e as SpatialElementTag;
            if (spatial != null && spatial.HasLeader)
                AddHandle(e, "Колено пространственной марки", x => ((SpatialElementTag)x).LeaderElbow,
                    (x,p) => ((SpatialElementTag)x).LeaderElbow = p);
            TextNote note = e as TextNote;
            if (note != null)
                for (int i = 0; i < note.GetLeaders().Count; ++i)
                {
                    int index = i;
                    AddHandle(e, "Конец выноски текста " + i, x => ((TextNote)x).GetLeaders()[index].End,
                        (x,p) => ((TextNote)x).GetLeaders()[index].End = p);
                    AddHandle(e, "Колено выноски текста " + i, x => ((TextNote)x).GetLeaders()[index].Elbow,
                        (x,p) => ((TextNote)x).GetLeaders()[index].Elbow = p);
                }
        }
        public void RestoreHandles(Document doc, XYZ delta)
        {
            Element e = doc.GetElement(Id);
            View view = doc.GetElement(ViewId) as View;
            foreach (PointHandle handle in Handles) handle.Restore(e, delta, view);
        }
    }

    internal sealed class PointHandle
    {
        private readonly string name;
        private readonly XYZ before;
        private readonly Func<Element, XYZ> read;
        private readonly Action<Element, XYZ> write;
        public PointHandle(string name, XYZ before, Func<Element, XYZ> read, Action<Element, XYZ> write)
        { this.name = name; this.before = before; this.read = read; this.write = write; }
        public void Restore(Element e, XYZ delta, View view)
        {
            if (!e.ViewSpecific || view == null || e.OwnerViewId != view.Id)
                throw new InvalidOperationException("Корректировка оформления вне своего вида: " + name);
            XYZ current = read(e);
            XYZ residual = RelocationEngine.InViewOnly(
                ElementState.Project(before + delta - current, ElementState.Normal(view)), ElementState.Normal(view), name);
            if (residual.GetLength() > RelocationEngine.Tolerance) write(e, current + residual);
        }
        public void Verify(Element e, XYZ delta, View view)
        {
            XYZ current = read(e);
            XYZ error = ElementState.Project(before + delta - current, ElementState.Normal(view));
            if (error.GetLength() > RelocationEngine.Tolerance)
                throw new InvalidOperationException("Не сохранено оформление: " + name + " / " + RelocationReport.Describe(e) +
                    "; группа " + e.GroupId.IntegerValue + "; вид " + e.OwnerViewId.IntegerValue +
                    "; ошибка X " + (error.X * 304.8).ToString("F3") +
                    ", Y " + (error.Y * 304.8).ToString("F3") +
                    ", Z " + (error.Z * 304.8).ToString("F3") + " мм.");
        }
    }

    internal sealed class ViewState
    {
        public readonly ElementId Id;
        public readonly XYZ Center;
        public readonly XYZ Origin;
        public readonly XYZ Direction;
        public readonly bool Active;
        public ViewState(View view)
        {
            Id = view.Id; Origin = view.Origin; Direction = view.ViewDirection;
            Active = view.CropBoxActive; Center = GetCenter(view);
        }
        public static XYZ GetCenter(View view)
        {
            BoundingBoxXYZ box = view.CropBox;
            if (box == null) throw new InvalidOperationException("Не читается область обрезки: " + view.Name);
            return box.Transform.OfPoint((box.Min + box.Max).Multiply(0.5));
        }
    }
}









