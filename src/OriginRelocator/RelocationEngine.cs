using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using static Arv.OriginRelocator.RelocationProgress;

namespace Arv.OriginRelocator
{
    internal static class RelocationEngine
    {
        // 0.1 mm, expressed in Revit internal feet.
        public const double Tolerance = 0.1 / 304.8;

        public static void Execute(Document doc, RelocationPlan plan, XYZ delta, RelocationReport report)
        {
            if (Math.Abs(delta.Z) > 1e-12) throw new ArgumentException("Перенос по высоте отключён.");
            report.Lines.Add(string.Format(CultureInfo.InvariantCulture,
                "Вектор в мм: {0:R}; {1:R}; {2:R}", delta.X * 304.8, delta.Y * 304.8, delta.Z * 304.8));
            Check(report, null, "Сведения о листах", () => report.CaptureContexts(doc, plan));
            using (TransactionGroup group = new TransactionGroup(doc, "ARV — модель в начало координат"))
            {
                if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Не удалось начать операцию.");
                try
                {
                    bool modelKept = RunTransaction(doc, "Перенос модели", report, plan, () =>
                    {
                        foreach (KeyValuePair<int, bool> pin in plan.Pins.Where(x => x.Value))
                            TryElement(doc, report, new ElementId(pin.Key), "Снятие закрепления", () =>
                            { if (!plan.Ancestors(pin.Key).Any(id => doc.GetElement(new ElementId(id)) is Group)) doc.GetElement(new ElementId(pin.Key)).Pinned = false; });
                        MoveBestEffort(doc, plan.ModelRoots, delta, "Модель", report);
                        doc.Regenerate();
                        // Work-plane-based instances can inherit only the normal component from the moved plane.
                        // Move only the remaining displacement; keep their original host.
                        CorrectModelRemainders(doc, plan, delta, report);
                        foreach (ElementState s in plan.Models) Check(report, s.Id, "Проверка модели", () => s.Verify(doc, delta));
                        VerifyStationary(doc, plan, report);
#if SECTION_PROBE
                        SectionProbeTrace.Record(doc, plan, delta, report, "AFTER_MODEL");
#endif
                    });

                    if (!modelKept)
                    {
                        foreach (var root in plan.ModelRoots.ToList())
                            RunTransaction(doc, "Перенос модели / " + root.IntegerValue, report, plan, () =>
                            {
                                foreach (var pin in plan.Pins.Where(p => p.Value && (p.Key == root.IntegerValue || plan.Ancestors(p.Key).Contains(root.IntegerValue))))
                                    TryElement(doc, report, new ElementId(pin.Key), "Снятие закрепления", () =>
                                    { if (!plan.Ancestors(pin.Key).Any(id => doc.GetElement(new ElementId(id)) is Group)) doc.GetElement(new ElementId(pin.Key)).Pinned = false; });
                                Move(doc, new[] {root}, delta, "Модель", report);
                                doc.Regenerate();
                            });

                        RunTransaction(doc, "Остатки переноса модели", report, plan, () => CorrectModelRemainders(doc, plan, delta, report));
                    }

                    RunTransaction(doc, "Перенос видов и областей обрезки", report, plan, () =>
                    {

                        foreach (ElementId id in plan.ElevationMarkers)
                        {
                            TryElement(doc, report, id, "Обозначение фасада", () =>
                            {

                                // Markers are not model roots; move only if a model parent has not moved them.
                                // An explicit before snapshot is supplied by the plan.
                                ElementState original;
                                if (plan.MarkerStates.TryGetValue(id.IntegerValue, out original))
                                {
                                    XYZ residual = original.Residual(doc, delta, false);
                                    if (residual.GetLength() > Tolerance) Move(doc, new[] { id }, residual, "Обозначение фасада", report);
                                }

                            });
                        }
                        doc.Regenerate();
                        foreach (ViewState state in OrderViewsForRelocation(doc, plan.Views))
                        {
                            TryElement(doc, report, state.Id, "Перенос вида", () =>
                            {
                                View view = doc.GetElement(state.Id) as View;
                                ViewSection section = view as ViewSection;
                                XYZ residual = state.Center + delta - ViewState.GetCenter(view);
                                double normalError = (state.Origin + delta - view.Origin).DotProduct(state.Direction);
                                if (section != null && (Math.Abs(normalError) > Tolerance ||
                                    (plan.SectionViewElements.ContainsKey(view.Id.IntegerValue) && new XYZ(residual.X, residual.Y, 0).GetLength() > Tolerance)))
                                {
                                    ElementId viewElementId;
                                    if (!plan.SectionViewElements.TryGetValue(view.Id.IntegerValue, out viewElementId))
                                    {
                                        ElementId primary = view.GetPrimaryViewId();
                                        if (primary == ElementId.InvalidElementId ||
                                            !plan.SectionViewElements.TryGetValue(primary.IntegerValue, out viewElementId))
                                            throw new InvalidOperationException("Не найдено однозначное обозначение вида: " + view.Name +
                                                " (" + view.Id.IntegerValue + "). Подробности в отчёте.");
                                    }
                                    // Translate the entire section marker, including its manually adjusted line extents.
                                    // Dependent views without their own marker inherit only the parent's plane correction.
                                    XYZ planeMove = SectionMarkerTranslation(residual, state.Direction, normalError,
                                        plan.SectionViewElements.ContainsKey(view.Id.IntegerValue));
                                    Move(doc, new[] { viewElementId }, planeMove, "Плоскость вида " + view.Name, report);
                                    doc.Regenerate();
#if SECTION_PROBE
                                    SectionProbeTrace.Record(doc, plan, delta, report, "AFTER_SECTION_MARKER " + view.Id.IntegerValue, view.Id.IntegerValue);
#endif
                                    normalError = (state.Origin + delta - view.Origin).DotProduct(state.Direction);
                                    if (Math.Abs(normalError) > Tolerance)
                                        report.AddIssue(state.Id, "Плоскость вида", "Расхождение " + (normalError * 304.8).ToString("F3") + " мм; обозначение " + viewElementId.IntegerValue);
                                    residual = state.Center + delta - ViewState.GetCenter(view);
                                }
                                XYZ inPlane = ElementState.Project(residual, view.ViewDirection);
                                if (inPlane.GetLength() > Tolerance)
                                {
                                    TranslateCrop(view, inPlane);
                                    doc.Regenerate();
                                }
#if SECTION_PROBE
                                SectionProbeTrace.Record(doc, plan, delta, report, "AFTER_VIEW " + view.Id.IntegerValue, view.Id.IntegerValue);
#endif
                                Check(report, state.Id, "Проверка вида", () => VerifyView(view, state, delta));

                            });
                        }
                        foreach (ElementId id in plan.SectionBoxes)
                        {
                            TryElement(doc, report, id, "Секущая рамка 3D", () =>
                            {
                                View3D view = (View3D)doc.GetElement(id);
                                BoundingBoxXYZ old = plan.BoxesBefore[id.IntegerValue];
                                BoundingBoxXYZ box = new BoundingBoxXYZ();
                                box.Min = old.Min; box.Max = old.Max;
                                box.Transform = Transform.CreateTranslation(delta).Multiply(old.Transform);
                                view.SetSectionBox(box);

                            });
                        }
                        doc.Regenerate();
                    });

                    RunTransaction(doc, "Перенос оформления видов", report, plan, () =>
                    {
                        foreach (ViewGraphicState graphic in plan.ViewGraphics.Where(g => g.IsReference))
                        {
                            TryElement(doc, report, graphic.State.Id, "Опорное обозначение вида", () =>
                            {
                                View owner = (View)doc.GetElement(graphic.State.ViewId);
                                XYZ remaining = graphic.Residual(doc, delta);
                                if (remaining.GetLength() > Tolerance)
                                {
                                    Move(doc, new[] { graphic.State.Id }, remaining, "Опорное обозначение вида", report, owner, true);
                                    doc.Regenerate();
                                }
                                Check(report, graphic.State.Id, "Проверка обозначения", () => graphic.Verify(doc, delta));

                            });
                        }
                        HashSet<int> annotationIds = new HashSet<int>(plan.Annotations.Select(s => s.Id.IntegerValue));
                        List<ElementState> roots = plan.Annotations
                            .Where(s => !plan.Ancestors(s.Id.IntegerValue).Any(id =>
                                annotationIds.Contains(id) || plan.Models.Any(m => m.Id.IntegerValue == id)))
                            .OrderBy(s => doc.GetElement(s.Id) is Dimension ? 1 : 0).ToList();
                        roots = roots.Where(root => Check(report, root.Id, "Корни оформления", () =>
                        {
                            Element e = doc.GetElement(root.Id);
                            Group parent = doc.GetElement(e.GroupId) as Group;
                            if (RelocationPlan.IsDetailGroup(parent))
                                throw new InvalidOperationException("Нельзя перемещать член группы оформления отдельно: " +
                                    RelocationReport.Describe(e) + "; группа " + parent.Id.IntegerValue);

                        })).ToList();
                        // Group by view and residual to preserve constraints during the move.
                        foreach (IGrouping<int, ElementState> viewGroup in roots.GroupBy(s => s.ViewId.IntegerValue))
                        {
                            var pending = viewGroup.ToList();
                            while (pending.Count > 0)
                            {
                                var first = pending[0]; XYZ residual = null;
                                if (!Check(report, first.Id, "Остаток оформления", () => residual = first.Residual(doc, delta, true))) { pending.RemoveAt(0); continue; }
                                var same = new List<ElementState>();
                                foreach (var state in pending.ToList())
                                {
                                    XYZ candidate = null;
                                    if (!Check(report, state.Id, "Остаток оформления", () => candidate = state.Residual(doc, delta, true))) { pending.Remove(state); continue; }
                                    if ((candidate - residual).GetLength() <= Tolerance) same.Add(state);
                                }
                                if (residual.GetLength() > Tolerance && same.Count > 0)
                                {
                                    TryElement(doc, report, first.ViewId, "Оформление вида", () =>
                                    {
                                        var ownerView = (View)doc.GetElement(first.ViewId);
                                        var moveIds = CollectAnnotationMoveRoots(doc, same.Select(state => state.Id).ToList(), plan.OriginalIds);
                                        MoveBestEffort(doc, moveIds, residual, "Оформление вида " + ownerView.Name, report, ownerView);
                                        doc.Regenerate();
                                    });
                                }
                                foreach (var state in same) pending.Remove(state);
                            }
                        }
                        report.Lines.Add("ВОССТАНОВЛЕНИЕ ПОЛОЖЕНИЯ ТЕКСТОВ И ВЫНОСОК");
                        foreach (ElementState s in plan.Annotations)
                        {
                            TryElement(doc, report, s.Id, "Тексты и выноски", () =>
                            {
                                // Do not directly edit group members. Validate that the group brought them along.
                                if (!plan.Ancestors(s.Id.IntegerValue).Any(annotationIds.Contains))
                                    s.RestoreHandles(doc, delta);

                            });
                        }
                        doc.Regenerate();
#if SECTION_PROBE
                        SectionProbeTrace.Record(doc, plan, delta, report, "BEFORE_FINAL_GRAPHICS_CHECK");
#endif
                        report.Lines.Add("ИТОГОВАЯ ПРОВЕРКА ГЕОМЕТРИИ, ТЕКСТОВ И ВЫНОСОК");
                        foreach (ElementState s in plan.Annotations) Check(report, s.Id, "Проверка оформления", () => s.Verify(doc, delta, true));
                        foreach (ViewGraphicState graphic in plan.ViewGraphics) Check(report, graphic.State.Id, "Проверка обозначения", () => graphic.Verify(doc, delta));


                        foreach (KeyValuePair<int, bool> pin in plan.Pins)
                        {
                            TryElement(doc, report, new ElementId(pin.Key), "Восстановление закрепления", () =>
                            {
                                Element e = doc.GetElement(new ElementId(pin.Key));
                                if (e == null) throw new InvalidOperationException("Исчез закреплённый элемент " + pin.Key);
                                if (!plan.ViewportCenters.ContainsKey(pin.Key) && e.Pinned != pin.Value) e.Pinned = pin.Value;

                            });
                        }
                        doc.Regenerate();
                        VerifyAll(doc, plan, delta, report, false);
                    });

                    // Finish annotation geometry before evaluating sheet-space viewport outlines.
                    RunTransaction(doc, "Восстановление положения видов на листах", report, plan, () =>
                    {
                        RestoreViewports(doc, plan, report);
                        VerifyAll(doc, plan, delta, report);
                    });

                    // Report post-commit discrepancies without undoing completed stages.
                    VerifyAll(doc, plan, delta, report);
                    report.KeptChanges = group.Assimilate() == TransactionStatus.Committed && report.CommittedStages.Count > 0;
                    if (!report.KeptChanges) report.AddIssue(null, "Сохранение операции", "Revit не сохранил группу переноса.");
                    report.Lines.Add("ЗАВЕРШЕНО: сохранено этапов " + report.CommittedStages.Count + "; замечаний " + report.Issues.Count + "; удалено некорректных размеров " + report.DeletedInvalidDimensions.Count);
                }
                catch (Exception ex)
                {
                    report.AddIssue(null, ex is Autodesk.Revit.Exceptions.OperationCanceledException ? "Остановлено пользователем" : "Завершение переноса", ex.Message);
                    if (group.GetStatus() == TransactionStatus.Started)
                    {
                        try { report.KeptChanges = group.Assimilate() == TransactionStatus.Committed && report.CommittedStages.Count > 0; }
                        catch (Exception saveError) { report.AddIssue(null, "Сохранение выполненного", saveError.Message); }
                    }
                }
                if (!report.KeptChanges)
                {
                    report.CommittedStages.Clear(); report.InvalidDimensionDeletionRequests.Clear(); report.DeletedInvalidDimensions.Clear();
                    report.Lines.Add("Revit не позволил сохранить изменения этой операции.");
                }
            }
        }

        private static void CorrectModelRemainders(Document doc, RelocationPlan plan, XYZ delta, RelocationReport report)
        {
            foreach (ElementState s in plan.Models.OrderBy(x => plan.Ancestors(x.Id.IntegerValue).Count()))
                        {
                TryElement(doc, report, s.Id, "Остаток переноса модели", () =>
                {
                    XYZ residual = s.Residual(doc, delta, false);
                    if (residual.GetLength() <= Tolerance) return;
                    FamilyInstance family = doc.GetElement(s.Id) as FamilyInstance;
                    if (family != null && family.SuperComponent == null &&
                        family.GroupId == ElementId.InvalidElementId && !plan.ModelRoots.Contains(s.Id))
                    {
                        Move(doc, new[] { s.Id }, residual, "Остаток переноса элемента на основе", report);
                        doc.Regenerate();
                    }

                });
            }
        }

        internal static bool MatchesAnnotationScope(Element element, View owner, bool knownReference)
        {
            if (element == null || element is Level || owner == null) return false;
            return knownReference || RelocationPlan.AnnotationOwner(element) == owner.Id;
        }

        internal static List<ElementId> CollectAnnotationMoveRoots(Document doc, ICollection<ElementId> roots,
            HashSet<int> allowedProjectIds)
        {
            List<ElementId> result = new List<ElementId>();
            HashSet<int> seen = new HashSet<int>();
            foreach (ElementId id in roots)
            {
                if (!seen.Add(id.IntegerValue)) continue;
                if (!allowedProjectIds.Contains(id.IntegerValue))
                    throw new InvalidOperationException("Корень оформления исключён из исходного набора: " + id.IntegerValue);
                if (doc.GetElement(id) == null)
                    throw new InvalidOperationException("Исчез корень оформления " + id.IntegerValue);
                // Only roots: never add pinned/internal group members to the native move call.
                result.Add(id);
            }
            return result;
        }
        internal static XYZ SectionMarkerTranslation(XYZ remaining, XYZ direction, double planeOffset, bool ownsMarker)
        {
            if (!ownsMarker) return HorizontalPlaneMove(direction, planeOffset);
            XYZ translation = new XYZ(remaining.X, remaining.Y, 0);
            return translation + HorizontalPlaneMove(direction, planeOffset - translation.DotProduct(direction));
        }

        internal static XYZ LabelOffsetFromSheet(XYZ vector, ViewportRotation rotation)
        {
            if (rotation == ViewportRotation.Clockwise) return new XYZ(-vector.Y, vector.X, 0);
            if (rotation == ViewportRotation.Counterclockwise) return new XYZ(vector.Y, -vector.X, 0);
            return new XYZ(vector.X, vector.Y, 0);
        }

        internal static XYZ SheetOffset(XYZ current, XYZ expected)
        {
            // Sheet placement is two-dimensional. Viewport outline Z is not a sheet displacement.
            return new XYZ(current.X - expected.X, current.Y - expected.Y, 0);
        }

        internal static void RestoreViewports(Document doc, RelocationPlan plan, RelocationReport report)
        {
            // Revit recalculates outlines after sheet changes; always read a regenerated outline.
            // Related viewports can change during regeneration, so verify the entire set each pass.
            for (int pass = 1; pass <= 3; ++pass)
            {
                doc.Regenerate();
                int moved = 0;
                foreach (KeyValuePair<int, XYZ> item in plan.ViewportCenters)
                {
                    TryElement(doc, report, new ElementId(item.Key), "Позиция вида на листе", () =>
                    {
                        Viewport viewport = doc.GetElement(new ElementId(item.Key)) as Viewport;
                        if (viewport == null) throw new InvalidOperationException("Исчез вид на листе: " + item.Key);
                        if (SheetOffset(viewport.GetBoxCenter(), item.Value).GetLength() <= Tolerance) return;
                        if (viewport.Pinned)
                        {
                            viewport.Pinned = false;
                            doc.Regenerate();
                        }
                        XYZ before = viewport.GetBoxCenter();
                        viewport.SetBoxCenter(new XYZ(item.Value.X, item.Value.Y, before.Z));
                        doc.Regenerate();
                        ++moved;
                        report.Lines.Add(string.Format(CultureInfo.InvariantCulture,
                            "ПОЗИЦИЯ НА ЛИСТЕ: {0}; вид {1}; лист {2}; проход {3}; коррекция X {4:R}, Y {5:R} мм; остаток {6:R} мм",
                            item.Key, viewport.ViewId.IntegerValue, report.SheetNumber(viewport.SheetId), pass,
                            (item.Value.X - before.X) * 304.8, (item.Value.Y - before.Y) * 304.8,
                            SheetOffset(viewport.GetBoxCenter(), item.Value).GetLength() * 304.8));

                    });
                }
                foreach (ViewportLabelState label in plan.ViewportLabels.Values)
                {
                    TryElement(doc, report, label.Id, "Заголовок на листе", () =>
                    {
                        Viewport viewport = (Viewport)doc.GetElement(label.Id);
                        if (label.Matches(viewport)) return;
                        if (viewport.Pinned) { viewport.Pinned = false; doc.Regenerate(); }
                        label.Restore(doc, viewport);
                        report.Lines.Add("ЗАГОЛОВОК НА ЛИСТЕ: " + label.Id.IntegerValue + "; проход " + pass);

                    });
                }
                foreach (int id in plan.ViewportCenters.Keys)
                {
                    TryElement(doc, report, new ElementId(id), "Закрепление вида на листе", () =>
                    {
                        Viewport viewport = (Viewport)doc.GetElement(new ElementId(id));
                        bool pinned = plan.Pins[id];
                        if (viewport.Pinned != pinned) viewport.Pinned = pinned;

                    });
                }
                doc.Regenerate();
                bool stable = false;
                Check(report, null, "Стабильность компоновки листов", () => stable = plan.ViewportCenters.All(item =>
                    SheetOffset(((Viewport)doc.GetElement(new ElementId(item.Key))).GetBoxCenter(), item.Value).GetLength() <= Tolerance) &&
                    plan.ViewportLabels.Values.All(label => label.Matches((Viewport)doc.GetElement(label.Id))));
                report.Lines.Add("ВИДЫ НА ЛИСТАХ: проход " + pass + "; исправлено " + moved + "; позиции подтверждены " + stable);
                if (stable) return;
            }
            // Report remaining sheet discrepancies after the bounded restoration passes.
            VerifySheetViewports(doc, plan, report);
        }

        private static void VerifySheetViewports(Document doc, RelocationPlan plan, RelocationReport report)
        {
            foreach (var item in plan.ViewportCenters)
                Check(report, new ElementId(item.Key), "Проверка позиции на листе", () =>
                {
                    var one = new RelocationPlan(); one.ViewportCenters.Add(item.Key, item.Value);
                    VerifyViewportPositions(doc, one);
                });
        }

        internal static void VerifyViewportPositions(Document doc, RelocationPlan plan)
        {
            foreach (KeyValuePair<int, XYZ> item in plan.ViewportCenters)
            {
                Viewport viewport = doc.GetElement(new ElementId(item.Key)) as Viewport;
                if (viewport == null) throw new InvalidOperationException("Исчез вид на листе: " + item.Key);
                XYZ error = SheetOffset(viewport.GetBoxCenter(), item.Value);
                if (error.GetLength() > Tolerance)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "Сместился вид на листе: {0}; вид {1}; X {2:F3}, Y {3:F3} мм; расхождение XY {4:F3} мм.",
                        item.Key, viewport.ViewId.IntegerValue,
                        error.X * 304.8, error.Y * 304.8, error.GetLength() * 304.8));
            }
        }

        internal static IEnumerable<ViewState> OrderViewsForRelocation(Document doc, IEnumerable<ViewState> states)
        {
            // Native probe: regenerating a translated plan crop before its section marker
            // changes manually adjusted marker extents. Move section/elevation/detail views
            // before plan crops; retain parent-before-dependent ordering within each phase.
            return states.OrderBy(s => doc.GetElement(s.Id) is ViewPlan ? 1 : 0)
                .ThenBy(s => ViewDepth(doc.GetElement(s.Id) as View));
        }

        private static int ViewDepth(View view)
        {
            int depth = 0;
            HashSet<int> seen = new HashSet<int>();
            while (view != null && seen.Add(view.Id.IntegerValue))
            {
                ElementId parent = view.GetPrimaryViewId();
                if (parent == ElementId.InvalidElementId && view.IsCallout) parent = view.GetCalloutParentId();
                if (parent == ElementId.InvalidElementId) break;
                view = view.Document.GetElement(parent) as View; ++depth;
            }
            return depth;
        }
        private static void TranslateCrop(View view, XYZ vector)
        {
            vector = InViewOnly(vector, ElementState.Normal(view), "Область обрезки " + view.Name);
            using (ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager())
            {
                if (manager.ShapeSet && !manager.Split && manager.CanHaveShape)
                {
                    IList<CurveLoop> loops = manager.GetCropShape();
                    if (loops.Count != 1)
                        throw new InvalidOperationException("Не поддерживается составная область обрезки: " + view.Name);
                    CurveLoop shifted = CurveLoop.CreateViaTransform(loops[0], Transform.CreateTranslation(vector));
                    manager.SetCropShape(shifted);
                }
                else
                {
                    BoundingBoxXYZ box = view.CropBox;
                    XYZ local = box.Transform.Inverse.OfVector(vector);
                    box.Min += local; box.Max += local;
                    view.CropBox = box;
                }
            }
        }
        private static void VerifyView(View view, ViewState before, XYZ delta)
        {
            XYZ currentCenter = ViewState.GetCenter(view);

            if ((view.ViewDirection - before.Direction).GetLength() > 1e-9)
                throw new InvalidOperationException("Изменилось направление вида: " + view.Name);
            XYZ residual = before.Center + delta - currentCenter;
            // Plans have no meaningful crop-box Z; all requested translations have zero Z.
            if (ElementState.Project(residual, view.ViewDirection).GetLength() > Tolerance ||
                (view is ViewSection && Math.Abs((before.Origin + delta - view.Origin).DotProduct(before.Direction)) > Tolerance))
                throw new InvalidOperationException("Не подтверждён перенос вида: " + view.Name);
            if (view.CropBoxActive != before.Active)
                throw new InvalidOperationException("Изменилось состояние обрезки вида: " + view.Name);
        }
        private static void VerifyStationary(Document doc, RelocationPlan plan, RelocationReport report)
        {
            foreach (KeyValuePair<int, double> item in plan.LevelElevations)
            {
                Check(report, new ElementId(item.Key), "Проверка уровня", () =>
                {
                    Level level = doc.GetElement(new ElementId(item.Key)) as Level;
                    if (level == null || Math.Abs(level.ProjectElevation - item.Value) > 1e-9 ||
                        level.Pinned != plan.LevelPins[item.Key])
                        throw new InvalidOperationException("Изменился уровень " + item.Key + ". Требуется проверка.");

                });
            }
            foreach (ElementState s in plan.Stationary) Check(report, s.Id, "Неподвижный элемент", () => s.Verify(doc, XYZ.Zero));
            foreach (KeyValuePair<int, Transform> item in plan.LinkTransforms)
            {
                Check(report, new ElementId(item.Key), "Проверка связи", () =>
                {
                    Element e = doc.GetElement(new ElementId(item.Key));
                    Transform now = e is RevitLinkInstance ? ((RevitLinkInstance)e).GetTransform() : ((ImportInstance)e).GetTransform();
                    Transform old = item.Value;
                    if ((now.Origin - old.Origin).GetLength() > Tolerance ||
                        (now.BasisX - old.BasisX).GetLength() > 1e-9 ||
                        (now.BasisY - old.BasisY).GetLength() > 1e-9 ||
                        (now.BasisZ - old.BasisZ).GetLength() > 1e-9)
                        throw new InvalidOperationException("Сместилась связь " + item.Key + ". Требуется проверка.");

                });
            }
        }
        internal static void VerifyOriginalElements(Document doc, RelocationPlan plan, RelocationReport report)
        {
            List<int> missing = plan.OriginalIds.Where(id => !report.DeletedInvalidDimensions.Contains(id) && doc.GetElement(new ElementId(id)) == null).ToList();
            if (missing.Count == 0) return;
            List<string> descriptions = new List<string>();
            foreach (int id in missing)
            {
                string description;
                if (!plan.OriginalDescriptions.TryGetValue(id, out description)) description = id.ToString();
                descriptions.Add(description);
                report.AddIssue(new ElementId(id), "Сохранность элементов", "Исчез исходный элемент: " + description);
            }
        }

#if SECTION_PROBE
        internal static void TranslateCropForProbe(View view, XYZ delta) { TranslateCrop(view, delta); }
        internal static void VerifyViewForProbe(View view, ViewState before, XYZ delta) { VerifyView(view, before, delta); }
        internal static void VerifyProbeRestoration(Document doc, RelocationPlan plan, RelocationReport report)
        { VerifyAll(doc, plan, XYZ.Zero, report); }
#endif
        private static void VerifyAll(Document doc, RelocationPlan plan, XYZ delta, RelocationReport report, bool verifyViewports = true)
        {
            foreach (ElementState s in plan.Models) Check(report, s.Id, "Проверка модели", () => s.Verify(doc, delta));
            foreach (ElementState s in plan.Annotations) Check(report, s.Id, "Проверка оформления", () => s.Verify(doc, delta, true));
            foreach (ViewGraphicState graphic in plan.ViewGraphics) Check(report, graphic.State.Id, "Проверка обозначения", () => graphic.Verify(doc, delta));
            foreach (ViewState s in plan.Views) Check(report, s.Id, "Проверка вида", () => VerifyView((View)doc.GetElement(s.Id), s, delta));
            foreach (KeyValuePair<int, ElementState> s in plan.MarkerStates) Check(report, s.Value.Id, "Проверка фасада", () => s.Value.Verify(doc, delta));
            foreach (KeyValuePair<int, bool> pin in plan.Pins)
                Check(report, new ElementId(pin.Key), "Проверка закрепления", () =>
                { if ((verifyViewports || !plan.ViewportCenters.ContainsKey(pin.Key)) && doc.GetElement(new ElementId(pin.Key)).Pinned != pin.Value) throw new InvalidOperationException("Не восстановлено закрепление."); });
            VerifyStationary(doc, plan, report);
            foreach (ElementState s in plan.SheetStates) Check(report, s.Id, "Оформление листа", () => s.Verify(doc, XYZ.Zero));
            if (verifyViewports)
            {
                VerifySheetViewports(doc, plan, report);
                foreach (ViewportLabelState label in plan.ViewportLabels.Values) Check(report, label.Id, "Заголовок вида на листе", () => label.Verify((Viewport)doc.GetElement(label.Id)));
            }

            foreach (ElementId id in plan.SectionBoxes)
            {
                Check(report, id, "Проверка секущей рамки", () =>
                {
                    BoundingBoxXYZ old = plan.BoxesBefore[id.IntegerValue];
                    BoundingBoxXYZ now = ((View3D)doc.GetElement(id)).GetSectionBox();
                    if ((old.Transform.OfPoint(old.Min) + delta - now.Transform.OfPoint(now.Min)).GetLength() > Tolerance ||
                        (old.Transform.OfPoint(old.Max) + delta - now.Transform.OfPoint(now.Max)).GetLength() > Tolerance)
                        throw new InvalidOperationException("Не переместилась секущая рамка 3D-вида " + id.IntegerValue);

                });
            }
            VerifyOriginalElements(doc, plan, report);

        }

        internal static XYZ HorizontalOnly(XYZ vector, string stage)
        {
            if (Math.Abs(vector.Z) > Tolerance)
                throw new InvalidOperationException("Запрещено смещение по Z: " + stage +
                    "; Z = " + (vector.Z * 304.8).ToString("F3") + " мм.");
            return new XYZ(vector.X, vector.Y, 0);
        }

        internal static XYZ HorizontalPlaneMove(XYZ normal, double offset)
        {
            if (Math.Abs(offset) <= Tolerance) return XYZ.Zero;
            XYZ horizontalNormal = new XYZ(normal.X, normal.Y, 0);
            double lengthSquared = horizontalNormal.DotProduct(horizontalNormal);
            if (lengthSquared < 1e-12)
                throw new InvalidOperationException("Горизонтальную плоскость вида нельзя смещать по высоте.");
            return horizontalNormal.Multiply(offset / lengthSquared);
        }


        internal static XYZ InViewOnly(XYZ vector, XYZ normal, string stage)
        {
            double normalLengthSquared = normal.DotProduct(normal);
            if (normalLengthSquared < 1e-12)
                throw new InvalidOperationException("Не определена плоскость вида: " + stage);
            double outOfPlane = vector.DotProduct(normal) / Math.Sqrt(normalLengthSquared);
            if (Math.Abs(outOfPlane) > Tolerance)
                throw new InvalidOperationException("Смещение выходит из плоскости вида: " + stage +
                    "; отклонение = " + (outOfPlane * 304.8).ToString("F3") + " мм.");
            return vector;
        }

        private static void MoveBestEffort(Document doc, ICollection<ElementId> ids, XYZ vector, string stage, RelocationReport report, View owner = null)
        {
            if (ids.Count == 0) return;
            if (TryElement(doc, report, null, stage, () => Move(doc, ids, vector, stage, report, owner), false)) return;
            foreach (var id in ids)
                TryElement(doc, report, id, stage, () => Move(doc, new[] {id}, vector, stage, report, owner));
        }
        private static void Move(Document doc, ICollection<ElementId> ids, XYZ vector, string stage, RelocationReport report,
            View annotationView = null, bool referenceGraphic = false)
        {
            foreach (ElementId id in ids)
            {
                Element element = doc.GetElement(id);
                if (RelocationPlan.IsStationaryElement(element))
                    throw new InvalidOperationException("Неподвижный элемент попал в набор перемещения: " +
                        RelocationReport.Describe(element) + "; этап " + stage + ".");
            }
            if (annotationView == null)
                vector = HorizontalOnly(vector, stage);
            else
            {
                vector = InViewOnly(vector, ElementState.Normal(annotationView), stage);
                foreach (ElementId id in ids)
                {
                    Element e = doc.GetElement(id);
                    bool knownReference = referenceGraphic && annotationView.GetReferenceSections()
                        .Concat(annotationView.GetReferenceCallouts()).Concat(annotationView.GetReferenceElevations()).Contains(id);
                    if (!MatchesAnnotationScope(e, annotationView, knownReference))
                        throw new InvalidOperationException("В набор оформления попал элемент вне вида: " +
                            RelocationReport.Describe(e));
                }
            }
            if (ids.Count == 0 || vector.GetLength() <= Tolerance) return;
            if (ids.Any(id => doc.GetElement(id) is Level))
                throw new InvalidOperationException("Перемещение уровней запрещено.");
            report.Lines.Add(stage + ": " + ids.Count + " объектов.");
            foreach (Group group in ids.Select(id => doc.GetElement(id)).OfType<Group>())
                report.Lines.Add("ПЕРЕНОС ГРУППЫ: " + group.Id.IntegerValue + " / " + group.Name);
            report.Lines.Add(string.Format(CultureInfo.InvariantCulture,
                "ВЕКТОР ЭТАПА в мм: X {0:R}; Y {1:R}; Z {2:R}; режим {3}",
                vector.X * 304.8, vector.Y * 304.8, vector.Z * 304.8,
                annotationView == null ? "XY" : "плоскость вида " + annotationView.Id.IntegerValue));
            try { ElementTransformUtils.MoveElements(doc, ids, vector); }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                report.Lines.Add("Не перенесён набор (" + stage + "): " + string.Join(", ", ids.Select(x => x.IntegerValue)));
                throw new InvalidOperationException(stage + ": " + ex.Message, ex);
            }
        }
        private static bool RunTransaction(Document doc, string name, RelocationReport report, RelocationPlan plan, Action body)
        {
            report.CurrentStage = name;
            var requestsBefore = new HashSet<int>(report.InvalidDimensionDeletionRequests);
            using (Transaction transaction = new Transaction(doc, name))
            {
                try
                {
                    if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Не началась транзакция.");
                    var options = transaction.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new StrictFailures(report, plan)); options.SetClearAfterRollback(true);
                    transaction.SetFailureHandlingOptions(options);
                    body();
                    if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit не сохранил этот этап. Другие подтверждённые этапы сохраняются.");
                    report.CommittedStages.Add(name);
                    InvalidDimensionRecovery.AcceptCommittedDeletions(doc, plan, report);
                    VerifyOriginalElements(doc, plan, report);
                    return true;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    report.InvalidDimensionDeletionRequests.IntersectWith(requestsBefore);
                    throw;
                }
                catch (Exception ex)
                {
                    bool committed = transaction.GetStatus() == TransactionStatus.Committed;
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    if (!committed) report.InvalidDimensionDeletionRequests.IntersectWith(requestsBefore);
                    else if (!report.CommittedStages.Contains(name)) report.CommittedStages.Add(name);
                    report.AddIssue(null, name, ex.Message);
                    return committed;
                }
            }
        }
    }

}
