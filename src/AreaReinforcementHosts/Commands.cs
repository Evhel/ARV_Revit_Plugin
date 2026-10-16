using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ARV.Common;

namespace ARV.AreaReinforcementHosts
{
    internal static class SelectionSupport
    {
        internal static AreaReinforcement Resolve(Document doc, Element element)
        {
            var area = element as AreaReinforcement;
            var bar = element as RebarInSystem;
            return area ?? (bar == null ? null : doc.GetElement(bar.SystemId) as AreaReinforcement);
        }

        internal static void Report(UIApplication app, IList<RehostResult> results, string context)
        {
            var window = new ReportWindow(results, context);
            new WindowInteropHelper(window) { Owner = app.MainWindowHandle };
            window.ShowDialog();
        }
    }

    internal sealed class AreaFilter : ISelectionFilter
    {
        private readonly Document doc;
        private readonly bool includeSystemBars;
        internal AreaFilter(Document document, bool allowSystemBars = true) { doc = document; includeSystemBars = allowSystemBars; }
        public bool AllowElement(Element element)
        {
            return element is AreaReinforcement || (includeSystemBars && SelectionSupport.Resolve(doc, element) != null);
        }
        public bool AllowReference(Reference reference, XYZ point) { return false; }
    }

    internal sealed class HostFilter : ISelectionFilter
    {
        private readonly bool wallsOnly;
        internal HostFilter(bool wallOnly) { wallsOnly = wallOnly; }
        public bool AllowElement(Element element)
        {
            if (wallsOnly && !(element is Wall)) return false;
            return (element is Wall || element is Floor || element is Part) && RebarHostData.IsValidHost(element);
        }
        public bool AllowReference(Reference reference, XYZ point) { return false; }
    }

    [Transaction(TransactionMode.Manual)]
    public class RehostSingleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument ui, out Document doc)) return Result.Cancelled;
                var reference = ui.Selection.PickObject(ObjectType.Element, new AreaFilter(doc), "Выберите армирование по площади или стержень его системы. Esc — отмена.");
                var source = SelectionSupport.Resolve(doc, doc.GetElement(reference));
                var hostReference = ui.Selection.PickObject(ObjectType.Element, new HostFilter(false), "Выберите новую основу: конструктивную стену, перекрытие или подходящую часть. Esc — отмена.");
                var host = doc.GetElement(hostReference);
                var results = RehostService.Run(doc, new List<ElementId> { source.Id }, host, null);
                if (results.Any(result => result.Status != "Изменено"))
                    SelectionSupport.Report(data.Application, results, "Смена основы одного армирования по площади");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class RehostByFloorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument ui, out Document doc)) return Result.Cancelled;
                var selected = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Select(e => SelectionSupport.Resolve(doc, e)).Where(a => a != null)
                    .Select(a => a.Id).Distinct().ToList();
                if (selected.Count == 0)
                {
                    selected = ui.Selection.PickObjects(ObjectType.Element, new AreaFilter(doc, false),
                        "Выберите армирования по площади одного этажа и нажмите «Готово». Esc — отмена.")
                        .Select(picked => doc.GetElement(picked).Id).Distinct().ToList();
                    if (selected.Count == 0) return Result.Cancelled;
                }
                var reference = ui.Selection.PickObject(ObjectType.Element, new HostFilter(true), "Выберите стену нужного этажа. Её базовый уровень задаёт этаж поиска основ.");
                var wall = (Wall)doc.GetElement(reference);
                var level = HostGeometry.BaseLevel(doc, wall);
                var candidates = HostGeometry.WallsOnSameBaseLevel(doc, wall);
                var results = RehostService.Run(doc, selected, null, candidates);
                SelectionSupport.Report(data.Application, results, "Базовый уровень: «" + level.Name + "». Поиск стен по нижней границе контура.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}