using System;
using Autodesk.Revit.DB;

namespace Arv.OriginRelocator
{
    internal sealed class ViewportLabelState
    {
        public readonly ElementId Id;
        public readonly ViewportRotation Rotation;
        public readonly double LineLength;
        public readonly bool HasLabel;
        public readonly XYZ Minimum;
        public readonly XYZ Maximum;
        public ViewportLabelState(Viewport viewport)
        {
            Id = viewport.Id; Rotation = viewport.Rotation; LineLength = viewport.LabelLineLength;
            using (Outline outline = viewport.GetLabelOutline())
            {
                HasLabel = !outline.IsEmpty;
                if (HasLabel) { Minimum = outline.MinimumPoint; Maximum = outline.MaximumPoint; }
            }
        }
        public bool Matches(Viewport viewport)
        {
            if (viewport.Rotation != Rotation || Math.Abs(viewport.LabelLineLength - LineLength) > RelocationEngine.Tolerance)
                return false;
            using (Outline outline = viewport.GetLabelOutline())
            {
                if (HasLabel == outline.IsEmpty) return false;
                return !HasLabel ||
                    (RelocationEngine.SheetOffset(outline.MinimumPoint, Minimum).GetLength() <= RelocationEngine.Tolerance &&
                     RelocationEngine.SheetOffset(outline.MaximumPoint, Maximum).GetLength() <= RelocationEngine.Tolerance);
            }
        }
        public void Restore(Document doc, Viewport viewport)
        {
            if (viewport.Rotation != Rotation)
                throw new InvalidOperationException("Изменился поворот вида на листе: " + Id.IntegerValue);
            if (Math.Abs(viewport.LabelLineLength - LineLength) > RelocationEngine.Tolerance)
            {
                viewport.LabelLineLength = LineLength;
                doc.Regenerate();
            }
            using (Outline outline = viewport.GetLabelOutline())
            {
                if (HasLabel == outline.IsEmpty)
                    throw new InvalidOperationException("Изменилось наличие заголовка вида: " + Id.IntegerValue);
                if (!HasLabel) return;
                XYZ residual = RelocationEngine.SheetOffset(Minimum, outline.MinimumPoint);
                if (residual.GetLength() > RelocationEngine.Tolerance)
                {
                    // LabelOffset is expressed in the frame established with Rotation=None.
                    viewport.LabelOffset += RelocationEngine.LabelOffsetFromSheet(residual, Rotation);
                    doc.Regenerate();
                }
            }
        }
        public void Verify(Viewport viewport)
        {
            if (!Matches(viewport))
                throw new InvalidOperationException("Не сохранено положение или длина заголовка вида на листе: " + Id.IntegerValue);
        }
    }

    internal sealed class ViewGraphicState
    {
        public readonly ElementState State;
        public readonly bool IsReference;
        public readonly ElementId ReferencedView;
        private readonly XYZ[] GraphicPoints;
        public ViewGraphicState(Element element, View owner, bool reference)
        {
            State = ElementState.Capture(element, owner, true);
            if (State == null)
                throw new InvalidOperationException("Не читается обозначение вида: " + element.Id.IntegerValue +
                    "; родительский вид " + owner.Id.IntegerValue);
            GraphicPoints = ReadGraphicPoints(element, owner);
            IsReference = reference;
            ReferencedView = reference ? ReferenceableViewUtils.GetReferencedViewId(element.Document, element.Id) : ElementId.InvalidElementId;
        }
        private static XYZ[] ReadGraphicPoints(Element element, View owner)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(owner);
            return box == null ? null : new[] { box.Transform.OfPoint(box.Min), box.Transform.OfPoint(box.Max) };
        }
        public XYZ Residual(Document doc, XYZ delta)
        {
            XYZ[] current = ReadGraphicPoints(doc.GetElement(State.Id), (View)doc.GetElement(State.ViewId));
            if (GraphicPoints == null || current == null) return State.Residual(doc, delta, true);
            XYZ residual = ElementState.PointSetCenter(GraphicPoints) + delta - ElementState.PointSetCenter(current);
            return ElementState.Project(residual, ((View)doc.GetElement(State.ViewId)).ViewDirection);
        }
        public void Verify(Document doc, XYZ delta)
        {
            try
            {
                State.Verify(doc, delta, true);
                if (GraphicPoints != null)
                {
                    View owner = (View)doc.GetElement(State.ViewId);
                    XYZ[] current = ReadGraphicPoints(doc.GetElement(State.Id), owner);
                    if (current == null || !ElementState.TranslatedPointSetsMatch(GraphicPoints, current, delta,
                        owner.ViewDirection, RelocationEngine.Tolerance))
                        throw new InvalidOperationException("Не сместилась видимая графика обозначения разреза.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не сохранено обозначение " + State.Id.IntegerValue +
                    " на виде " + State.ViewId.IntegerValue + ": " + ex.Message, ex);
            }
            if (IsReference && ReferenceableViewUtils.GetReferencedViewId(doc, State.Id) != ReferencedView)
                throw new InvalidOperationException("Изменилась ссылка опорного обозначения: " + State.Id.IntegerValue);
        }
    }
}