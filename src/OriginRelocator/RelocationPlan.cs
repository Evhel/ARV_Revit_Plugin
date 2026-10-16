using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arv.OriginRelocator
{
    internal sealed class RelocationPlan
    {
        public readonly List<ElementState> Models = new List<ElementState>();
        public readonly List<ElementId> ModelRoots = new List<ElementId>();
        public readonly List<ElementState> Annotations = new List<ElementState>();
        public readonly List<ViewState> Views = new List<ViewState>();
        public readonly List<ElementId> ElevationMarkers = new List<ElementId>();
        public readonly Dictionary<int, int> Parent = new Dictionary<int, int>();
        public readonly Dictionary<int, bool> Pins = new Dictionary<int, bool>();
        public readonly List<ElementState> Stationary = new List<ElementState>();
        public readonly Dictionary<int, Transform> LinkTransforms = new Dictionary<int, Transform>();
        public readonly HashSet<int> OriginalIds = new HashSet<int>();
        public readonly Dictionary<int, string> OriginalDescriptions = new Dictionary<int, string>();
        public readonly HashSet<int> AnnotationViewIds = new HashSet<int>();
        public readonly List<ElementId> SectionBoxes = new List<ElementId>();
        public readonly Dictionary<int, BoundingBoxXYZ> BoxesBefore = new Dictionary<int, BoundingBoxXYZ>();
        public readonly Dictionary<int, ElementState> MarkerStates = new Dictionary<int, ElementState>();
        public readonly Dictionary<int, XYZ> ViewportCenters = new Dictionary<int, XYZ>();
        public readonly List<ElementState> SheetStates = new List<ElementState>();
        public readonly Dictionary<int, ViewportLabelState> ViewportLabels = new Dictionary<int, ViewportLabelState>();
        public readonly List<ViewGraphicState> ViewGraphics = new List<ViewGraphicState>();
        public readonly List<int> BlockedLinkGroups = new List<int>();
        public readonly HashSet<int> Excluded = new HashSet<int>();
        public readonly Dictionary<int, ElementId> SectionViewElements = new Dictionary<int, ElementId>();
        public readonly Dictionary<int, double> LevelElevations = new Dictionary<int, double>();
        public readonly Dictionary<int, bool> LevelPins = new Dictionary<int, bool>();

        public static bool IsModelView(View v)
        { return !v.IsTemplate && (v is ViewPlan || v is ViewSection); }

        public static RelocationPlan Create(Document doc, RelocationReport report)
        {
            RelocationPlan plan = new RelocationPlan();
            List<Element> all = CollectProjectElements(doc, report);
            foreach (Element e in all)
            {
                Workset workset = doc.GetWorksetTable().GetWorkset(e.WorksetId);
                plan.OriginalIds.Add(e.Id.IntegerValue);
                plan.OriginalDescriptions[e.Id.IntegerValue] = DescribeOriginal(e, workset);
            }
            foreach (Level level in all.OfType<Level>())
            {
                plan.LevelElevations[level.Id.IntegerValue] = level.ProjectElevation;
                plan.LevelPins[level.Id.IntegerValue] = level.Pinned;
            }
            HashSet<int> stationary = new HashSet<int>();
            foreach (Element e in all)
            {
                bool link = IsStationaryElement(e);
                if (!link) continue;
                stationary.Add(e.Id.IntegerValue);
                ElementState state = ElementState.Capture(e);
                if (state != null) plan.Stationary.Add(state);
                RevitLinkInstance revit = e as RevitLinkInstance;
                ImportInstance import = e as ImportInstance;
                if (revit != null) plan.LinkTransforms[e.Id.IntegerValue] = revit.GetTransform();
                if (import != null) plan.LinkTransforms[e.Id.IntegerValue] = import.GetTransform();
            }

            // Explicit ownership is stronger than the general logical-child API.
            foreach (Element e in all)
            {
                if (e.GroupId != ElementId.InvalidElementId) plan.SetParent(e.Id, e.GroupId);
                Group group = e as Group;
                if (group != null)
                {
                    foreach (ElementId member in group.GetMemberIds()) plan.SetParent(member, group.Id);
                    if (group.IsAttached) plan.SetParent(group.Id, group.AttachedParentId);
                }
                if (e.AssemblyInstanceId != ElementId.InvalidElementId) plan.SetParent(e.Id, e.AssemblyInstanceId);
                FamilyInstance family = e as FamilyInstance;
                if (family != null)
                {
                    if (family.SuperComponent != null) plan.SetParent(e.Id, family.SuperComponent.Id);
                    else if (family.Host != null && !(family.Host is Level) && !stationary.Contains(family.Host.Id.IntegerValue))
                        plan.SetParent(e.Id, family.Host.Id);
                }
                MultiSegmentGrid chain = e as MultiSegmentGrid;
                if (chain != null) foreach (ElementId child in chain.GetGridIds()) plan.SetParent(child, chain.Id);
                Sketch sketch = e as Sketch;
                if (sketch != null)
                {
                    foreach (ElementId child in sketch.GetAllElements()) plan.SetParent(child, sketch.OwnerId);
                    plan.Excluded.Add(sketch.Id.IntegerValue);
                }
                Stairs stairs = e as Stairs;
                if (stairs != null)
                {
                    if (stairs.MultistoryStairsId != ElementId.InvalidElementId) plan.SetParent(stairs.Id, stairs.MultistoryStairsId);
                    foreach (ElementId child in stairs.GetStairsRuns().Concat(stairs.GetStairsLandings())
                        .Concat(stairs.GetStairsSupports()).Concat(stairs.GetAssociatedRailings())) plan.SetParent(child, stairs.Id);
                }
                MultistoryStairs multi = e as MultistoryStairs;
                if (multi != null) foreach (ElementId child in multi.GetAllStairsIds()) plan.SetParent(child, multi.Id);
                Railing railing = e as Railing;
                if (railing != null && railing.HasHost) plan.SetParent(railing.Id, railing.HostId);
                Wall wall = e as Wall;
                if (wall != null && wall.CurtainGrid != null)
                    foreach (ElementId child in wall.CurtainGrid.GetPanelIds().Concat(wall.CurtainGrid.GetMullionIds())
                        .Concat(wall.CurtainGrid.GetUGridLineIds()).Concat(wall.CurtainGrid.GetVGridLineIds()))
                        plan.SetParent(child, wall.Id);
                MultiReferenceAnnotation mra = e as MultiReferenceAnnotation;
                if (mra != null) { plan.SetParent(mra.TagId, mra.Id); plan.SetParent(mra.DimensionId, mra.Id); }
            }

            foreach (Element e in all)
            {
                if (stationary.Contains(e.Id.IntegerValue) && e.GroupId != ElementId.InvalidElementId)
                    plan.BlockedLinkGroups.Add(e.GroupId.IntegerValue);
                if (stationary.Contains(e.Id.IntegerValue) || !CandidateModel(e, plan.Excluded)) continue;
                ElementState s = ElementState.Capture(e);
                if (s == null)
                { report.Lines.Add("Без размещённой геометрии: " + RelocationReport.Describe(e)); continue; }
                plan.Models.Add(s);
            }
            // Logical children include generated reinforcement, openings, hosted geometry etc.
            // View-specific children are handled separately; analytical models follow their physical hosts.
            HashSet<int> modelIds = new HashSet<int>(plan.Models.Select(x => x.Id.IntegerValue));
            foreach (ElementState s in plan.Models)
                foreach (ElementId child in doc.GetElement(s.Id).GetDependentElements(null))
                    if (modelIds.Contains(child.IntegerValue) && child != s.Id &&
                        !plan.Parent.ContainsKey(child.IntegerValue) && !plan.Ancestors(s.Id.IntegerValue).Contains(child.IntegerValue))
                        plan.SetParent(child, s.Id);
            foreach (ElementState s in plan.Models)
                if (!plan.Ancestors(s.Id.IntegerValue).Any(modelIds.Contains)) plan.ModelRoots.Add(s.Id);

            List<View> views = all.OfType<View>().Where(IsModelView).ToList();
            foreach (View v in views) if (v.CropBox != null) plan.Views.Add(new ViewState(v));
            CollectSectionViewElements(doc, all, views, plan, report);
            CollectViewGraphics(doc, all, views, plan, report);
            foreach (ElevationMarker marker in all.OfType<ElevationMarker>())
                {
                plan.ElevationMarkers.Add(marker.Id);
                ElementState state = ElementState.Capture(marker);
                if (state == null) throw new InvalidOperationException("Не читается положение фасадной марки " + marker.Id.IntegerValue);
                plan.MarkerStates.Add(marker.Id.IntegerValue, state);
            }

            foreach (Viewport viewport in all.OfType<Viewport>())
            {
                plan.ViewportCenters[viewport.Id.IntegerValue] = viewport.GetBoxCenter();
                plan.ViewportLabels[viewport.Id.IntegerValue] = new ViewportLabelState(viewport);
                View v = doc.GetElement(viewport.ViewId) as View;
                if (v == null || v.IsTemplate) continue;
                if (!IsModelView(v))
                {
                    report.Lines.Add("Вид на листе оставлен: " + v.ViewType + " / " + v.Name);
                    // Locked 3D annotation planes require a separate camera workflow: never silently skip annotations.
                    if (v is View3D && all.Any(e => e.ViewSpecific && e.OwnerViewId == v.Id && IsAnnotation(e)))
                        throw new InvalidOperationException("На 3D-виде «" + v.Name +
                            "» есть аннотации. Первая версия переносит оформление планов, разрезов, фасадов и узлов; " +
                            "для аннотаций 3D-вида требуется отдельная поддержка.");
                    continue;
                }
                plan.AnnotationViewIds.Add(v.Id.IntegerValue);
                ElementId primary = v.GetPrimaryViewId();
                if (primary != ElementId.InvalidElementId) plan.AnnotationViewIds.Add(primary.IntegerValue);
            }
            // Ownerview filtering includes hidden annotations and avoids visibility-based omission.
            List<string> annotationIssues = new List<string>();
            foreach (Element e in all)
            {
                if (!IsAnnotation(e) || plan.Excluded.Contains(e.Id.IntegerValue)) continue;
                ElementId ownerId = AnnotationOwner(e);
                if (!plan.AnnotationViewIds.Contains(ownerId.IntegerValue)) continue;
                View owner = doc.GetElement(ownerId) as View;
                ElementState state = ElementState.Capture(e, owner, true);
                if (state != null) plan.Annotations.Add(state);
                else
                {
                    string category = e.Category == null ? "(нет)" :
                        e.Category.Name + " / " + e.Category.Id.IntegerValue + " / " +
                        (Enum.GetName(typeof(BuiltInCategory), e.Category.Id.IntegerValue) ?? "(неизвестная категория)");
                    string issue = RelocationReport.Describe(e) + " / категория: " + category +
                        " / вид: " + owner.Name + " (" + owner.Id.IntegerValue + ")";
                    annotationIssues.Add(issue);
                    report.Lines.Add("НЕ ОПРЕДЕЛЕНО ПОЛОЖЕНИЕ ОФОРМЛЕНИЯ: " + issue);
                }
            }
            foreach (ElementState state in plan.Annotations.Where(s => doc.GetElement(s.Id) is Group))
            {
                Group group = (Group)doc.GetElement(state.Id);
                report.Lines.Add("ГРУППА ОФОРМЛЕНИЯ: " + group.Id.IntegerValue + " / " + group.Name +
                    " / вид " + state.ViewId.IntegerValue + " / членов " + group.GetMemberIds().Count +
                    " / присоединена " + group.IsAttached + " / основа " + group.AttachedParentId.IntegerValue);
            }
            if (annotationIssues.Count > 0)
                throw new InvalidOperationException("Не удалось определить положение оформления (" + annotationIssues.Count +
                    " элементов):\n" + string.Join("\n", annotationIssues.Take(5)) +
                    "\nПолный список записан в отчёт.");
            HashSet<int> sheetIds = new HashSet<int>(all.OfType<ViewSheet>().Select(s => s.Id.IntegerValue));
            foreach (Element e in all.Where(e => e.ViewSpecific && sheetIds.Contains(e.OwnerViewId.IntegerValue) && IsAnnotation(e)))
            {
                ElementState state = ElementState.Capture(e, doc.GetElement(e.OwnerViewId) as View, true);
                if (state != null) plan.SheetStates.Add(state);
            }
            foreach (View3D v in all.OfType<View3D>().Where(v => !v.IsTemplate && v.IsSectionBoxActive))
            { plan.SectionBoxes.Add(v.Id); plan.BoxesBefore[v.Id.IntegerValue] = v.GetSectionBox(); }

            HashSet<int> movable = new HashSet<int>(plan.Models.Select(x => x.Id.IntegerValue)
                .Concat(plan.Annotations.Select(x => x.Id.IntegerValue))
                .Concat(plan.ElevationMarkers.Select(x => x.IntegerValue))
                .Concat(plan.Views.Select(x => x.Id.IntegerValue)).Concat(plan.ViewportCenters.Keys)
                .Concat(plan.SectionViewElements.Values.Select(x => x.IntegerValue))
                .Concat(plan.ViewGraphics.Select(x => x.State.Id.IntegerValue)));
            foreach (int id in movable) plan.Pins[id] = doc.GetElement(new ElementId(id)).Pinned;
            if (plan.BlockedLinkGroups.Any())
                throw new InvalidOperationException("Связь входит в группу: " + string.Join(", ", plan.BlockedLinkGroups.Distinct()) +
                    ". Перенос группы переместил бы связь. Исключите связь из группы перед запуском.");
            if (plan.ModelRoots.Count == 0) throw new InvalidOperationException("Не найдены размещённые элементы модели.");
            report.Lines.Add("Модель: " + plan.Models.Count + "; корни: " + plan.ModelRoots.Count +
                "; виды: " + plan.Views.Count + "; оформление: " + plan.Annotations.Count);
            foreach (ElementState s in plan.Models)
                report.Lines.Add((plan.ModelRoots.Contains(s.Id) ? "ПЕРЕНОС: " : "С ОСНОВОЙ: ") + RelocationReport.Describe(doc.GetElement(s.Id)));
            return plan;
        }

        private static void CollectSectionViewElements(Document doc, IList<Element> all,
            IEnumerable<View> views, RelocationPlan plan, RelocationReport report)
        {
            List<Element> viewers = all.Where(e => !(e is View) && e.Category != null &&
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Viewers).Where(e =>
                {
                    Parameter reference = e.get_Parameter(BuiltInParameter.VIEWER_IS_REFERENCE);
                    return reference == null || reference.StorageType != StorageType.Integer || reference.AsInteger() == 0;
                }).ToList();
            ElementClassFilter viewFilter = new ElementClassFilter(typeof(View));
            Dictionary<int, IList<ElementId>> dependencies = viewers.ToDictionary(e => e.Id.IntegerValue,
                e => e.GetDependentElements(viewFilter));
            foreach (ViewSection view in views.OfType<ViewSection>())
            {
                List<Element> candidates = viewers.Where(e => dependencies[e.Id.IntegerValue].Contains(view.Id)).ToList();
                // The view-name parameter is not localized. Names are never used to skip a view.
                if (candidates.Count != 1)
                {
                    List<Element> named = viewers.Where(e =>
                    {
                        Parameter name = e.get_Parameter(BuiltInParameter.VIEW_NAME);
                        string declared = name != null && name.StorageType == StorageType.String ? name.AsString() : e.Name;
                        return string.Equals(declared, view.Name, StringComparison.Ordinal);
                    }).ToList();
                    List<Element> typed = named.Where(e => e.GetTypeId() == view.GetTypeId()).ToList();
                    candidates = typed.Count > 0 ? typed : named;
                }
                if (candidates.Count == 1)
                {
                    plan.SectionViewElements[view.Id.IntegerValue] = candidates[0].Id;
                    report.Lines.Add("ОБОЗНАЧЕНИЕ ВИДА: " + view.Id.IntegerValue + " / " + view.Name +
                        " -> " + candidates[0].Id.IntegerValue);
                }
                else
                    report.Lines.Add("ОБОЗНАЧЕНИЕ ВИДА НЕ ОПРЕДЕЛЕНО: " + view.Id.IntegerValue + " / " + view.Name +
                        " / кандидаты: " + string.Join(", ", candidates.Select(e => e.Id.IntegerValue)));
            }
        }


        private static void CollectViewGraphics(Document doc, IList<Element> all, IList<View> views,
            RelocationPlan plan, RelocationReport report)
        {
            HashSet<int> allowed = new HashSet<int>(all.Select(e => e.Id.IntegerValue));
            HashSet<int> canonical = new HashSet<int>(plan.SectionViewElements.Values.Select(id => id.IntegerValue));
            HashSet<int> references = new HashSet<int>();
            foreach (View owner in views)
            {
                foreach (ElementId id in owner.GetReferenceSections().Concat(owner.GetReferenceCallouts()).Concat(owner.GetReferenceElevations()))
                {
                    if (!allowed.Contains(id.IntegerValue)) continue;
                    Element element = doc.GetElement(id);
                    View actualOwner = doc.GetElement(element.OwnerViewId) as View;
                    View host = actualOwner != null && IsModelView(actualOwner) ? actualOwner : owner;
                    if (!references.Add(id.IntegerValue)) continue;
                    ViewGraphicState state = new ViewGraphicState(element, host, true);
                    plan.ViewGraphics.Add(state);
                    report.Lines.Add("ОПОРНОЕ ОБОЗНАЧЕНИЕ: " + id.IntegerValue + " / родитель " + host.Id.IntegerValue +
                        " / целевой вид " + state.ReferencedView.IntegerValue);
                }
                using (FilteredElementCollector visible = new FilteredElementCollector(doc, owner.Id))
                {
                    foreach (ElementId id in visible.OfCategory(BuiltInCategory.OST_Viewers).ToElementIds())
                    {
                        if (!allowed.Contains(id.IntegerValue) || !canonical.Contains(id.IntegerValue)) continue;
                        Element element = doc.GetElement(id);
                        ViewGraphicState state = new ViewGraphicState(element, owner, false);
                        plan.ViewGraphics.Add(state);
                        report.Lines.Add("ГРАФИКА РАЗРЕЗА: " + id.IntegerValue + " / родитель " + owner.Id.IntegerValue);
                    }
                }
            }
        }

        internal static bool IsWarningsWorkset(Workset workset)
        {
            if (workset == null) return false;
            string name = (workset.Name ?? "").Trim().Replace("ё", "е").Replace("Ё", "Е");
            return string.Equals(name, "Серьезные предупреждения", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<Element> CollectProjectElements(Document doc, RelocationReport report)
        {
            // Filter by workset records before materializing any Element objects.
            // WorksetKind is intentionally irrelevant: this project's warnings are StandardWorkset.
            using (FilteredWorksetCollector worksets = new FilteredWorksetCollector(doc))
            {
                List<Workset> ignored = worksets.Where(IsWarningsWorkset).ToList();
                using (FilteredElementCollector collector = new FilteredElementCollector(doc))
                {
                    collector.WhereElementIsNotElementType();
                    foreach (Workset workset in ignored)
                    {
                        collector.WherePasses(new ElementWorksetFilter(workset.Id, true));
                        report.Lines.Add("РАБОЧИЙ НАБОР ИСКЛЮЧЁН ДО СБОРА ЭЛЕМЕНТОВ: " + workset.Name +
                            " / " + workset.Kind + " / " + workset.Id.IntegerValue);
                    }
                    return collector.ToElements().ToList();
                }
            }
        }
        private static string DescribeOriginal(Element e, Workset workset)
        {
            string category = e.Category == null ? "(нет)" : e.Category.Name + " / " +
                e.Category.Id.IntegerValue + " / " + e.Category.CategoryType;
            return RelocationReport.Describe(e) + "; категория " + category +
                "; рабочий набор " + (workset == null ? "(нет)" : workset.Name + " / " + workset.Kind) +
                "; вид " + e.OwnerViewId.IntegerValue + "; группа " + e.GroupId.IntegerValue +
                "; тип " + e.GetTypeId().IntegerValue;
        }

        internal static bool IsDetailGroup(Element e)
        {
            return e is Group && e.Category != null &&
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_IOSDetailGroups;
        }

        internal static ElementId AnnotationOwner(Element e)
        {
            if (e == null) return ElementId.InvalidElementId;
            if ((e.ViewSpecific || IsDetailGroup(e)) && e.OwnerViewId != ElementId.InvalidElementId)
                return e.OwnerViewId;
            if (!IsDetailGroup(e)) return ElementId.InvalidElementId;
            Group group = (Group)e;
            List<ElementId> owners = group.GetMemberIds().Select(id => e.Document.GetElement(id))
                .Where(member => member != null && member.ViewSpecific &&
                    member.OwnerViewId != ElementId.InvalidElementId)
                .Select(member => member.OwnerViewId).Distinct().Take(2).ToList();
            return owners.Count == 1 ? owners[0] : ElementId.InvalidElementId;
        }


        internal static bool IsStationaryElement(Element e)
        {
            return e is RevitLinkInstance || (e is ImportInstance && ((ImportInstance)e).IsLinked) ||
                e is PointCloudInstance || e is BasePoint || e is InternalOrigin;
        }

        // Datums and scope boxes may exist in an otherwise empty project template.
        // They remain candidates for the real relocation, especially when they host families.
        internal static bool IsTestProjectDatum(Element e)
        {
            return e is ReferencePlane || e is Grid || e is MultiSegmentGrid ||
                (e.Category != null && e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_VolumeOfInterest);
        }

        // Document/view definitions can have Model or Annotation categories and an OwnerViewId,
        // but they are settings rather than geometry that can be translated.
        private static readonly HashSet<int> SunGraphicCategories = new HashSet<int>
        {
            (int)BuiltInCategory.OST_SunStudy, (int)BuiltInCategory.OST_Sun,
            (int)BuiltInCategory.OST_SunPath1, (int)BuiltInCategory.OST_SunPath2,
            (int)BuiltInCategory.OST_SunSurface, (int)BuiltInCategory.OST_SunriseText,
            (int)BuiltInCategory.OST_SunsetText, (int)BuiltInCategory.OST_CompassInner,
            (int)BuiltInCategory.OST_CompassOuter, (int)BuiltInCategory.OST_CompassSection,
            (int)BuiltInCategory.OST_CompassSectionFilled, (int)BuiltInCategory.OST_CompassPrimaryMonth,
            (int)BuiltInCategory.OST_CompassSecondaryMonth
        };

        private static bool IsSunPathGraphic(Element e)
        {
            // Native sun-path elements use the generic Element class; identify their category.
            for (Category category = e.Category; category != null; category = category.Parent)
                if (SunGraphicCategories.Contains(category.Id.IntegerValue)) return true;
            return false;
        }

        private static bool IsNonGeometricDefinition(Element e)
        {
            return IsSunPathGraphic(e) || e is SunAndShadowSettings || e is ParameterElement || e is GraphicsStyle ||
                e is Material || e is ViewSheetSet || e is ElementType;
        }

        internal static bool CandidateModel(Element e, HashSet<int> excluded)
        {
            if (IsDetailGroup(e) || IsNonGeometricDefinition(e) || IsStationaryElement(e) || e.ViewSpecific || e is View || e is Level || e is SketchPlane || e is Sketch ||
                e is ElevationMarker || e is CurtainGridLine || excluded.Contains(e.Id.IntegerValue)) return false;
            if (e is Group || e is AssemblyInstance || e is Grid || e is MultiSegmentGrid ||
                e is ReferencePlane || e is Stairs || e is MultistoryStairs) return true;
            if (e.Category == null || e.Category.CategoryType != CategoryType.Model ||
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Cameras) return false;
            string category = Enum.GetName(typeof(BuiltInCategory), e.Category.Id.IntegerValue) ?? "";
            if (category.StartsWith("OST_IOS") || category.Contains("Analytical") || category.Contains("Analytic") ||
                category.Contains("Sketch") || category == "OST_Views" || category == "OST_Viewers") return false;
            // System definitions and containers without geometry are rejected by ElementState.Capture.
            return true;
        }
        private static bool IsAnnotation(Element e)
        {
            // Stationary links can be view-specific CAD instances; never treat them as movable detail.
            if (IsStationaryElement(e)) return false;
            // Detail groups have an OST_IOS* category, but are movable annotation containers.
            if (e is Group) return IsDetailGroup(e);
            if (IsNonGeometricDefinition(e) || e is View || e is Viewport || e is SketchPlane || e is Sketch || e is ElevationMarker) return false;
            if (e.Category == null) return e is Group;
            string cat = Enum.GetName(typeof(BuiltInCategory), e.Category.Id.IntegerValue) ?? "";
            if (cat == "OST_Viewers" || cat == "OST_ReferenceViewer" || cat == "OST_Callouts" ||
                cat == "OST_Cameras" || cat == "OST_Views" || cat == "OST_Levels" || cat == "OST_Grids" ||
                cat.StartsWith("OST_IOS") || cat.Contains("Sketch")) return false;
            return e is Group || e.Category.CategoryType == CategoryType.Annotation ||
                e.Category.CategoryType == CategoryType.Model;
        }
        private void SetParent(ElementId child, ElementId parent)
        {
            if (child == null || parent == null || child == ElementId.InvalidElementId ||
                parent == ElementId.InvalidElementId || child == parent) return;
            if (!Parent.ContainsKey(child.IntegerValue)) Parent.Add(child.IntegerValue, parent.IntegerValue);
        }
        public IEnumerable<int> Ancestors(int id)
        {
            HashSet<int> seen = new HashSet<int> { id };
            int parent;
            while (Parent.TryGetValue(id, out parent))
            {
                if (!seen.Add(parent)) throw new InvalidOperationException("Циклическая зависимость элементов: " + parent);
                yield return parent; id = parent;
            }
        }
    }
}










