using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace Arv.OriginRelocator
{
    internal sealed partial class RelocationReport
    {
        public readonly List<string> Issues = new List<string>();
        private readonly HashSet<string> issueKeys = new HashSet<string>();
        private Dictionary<int,string> sheetNumbers = new Dictionary<int,string>();
        public string SheetNumber(ElementId id) { string number; return sheetNumbers.TryGetValue(id.IntegerValue, out number) ? number : "не определён (ID " + id.IntegerValue + ")"; }
        private readonly Dictionary<int,string> contexts = new Dictionary<int,string>();
        public readonly List<string> CommittedStages = new List<string>();
        public string CurrentStage;
        public bool KeptChanges = false;
        public void AddIssue(ElementId id, string stage, string message)
        {
            string key = (id == null ? stage : id.IntegerValue.ToString()) + "|" + message;
            if (!issueKeys.Add(key)) return;
            string context;
            if (id == null || !contexts.TryGetValue(id.IntegerValue, out context)) context = id == null ? "" : "ID: " + id.IntegerValue;
            string line = stage + (context.Length == 0 ? "" : " / " + context) + ": " + message;
            Issues.Add(line); Lines.Add("ОШИБКА / " + line);
        }
        public void CaptureContexts(Document doc, RelocationPlan plan)
        {
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToDictionary(s => s.Id.IntegerValue, s => s.SheetNumber);
            sheetNumbers = sheets;
            var placed = new Dictionary<int,HashSet<string>>();
            foreach (var port in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                string number;
                if (!sheets.TryGetValue(port.SheetId.IntegerValue, out number)) continue;
                HashSet<string> numbers;
                if (!placed.TryGetValue(port.ViewId.IntegerValue, out numbers)) placed[port.ViewId.IntegerValue] = numbers = new HashSet<string>();
                numbers.Add(number);
            }
            // An annotation in a primary view can also appear in placed dependent views.
            foreach (var entry in placed.ToList())
            {
                var view = doc.GetElement(new ElementId(entry.Key)) as View;
                if (view == null) continue;
                var parent = view.GetPrimaryViewId();
                if (parent == ElementId.InvalidElementId) continue;
                HashSet<string> numbers;
                if (!placed.TryGetValue(parent.IntegerValue, out numbers)) placed[parent.IntegerValue] = numbers = new HashSet<string>();
                numbers.UnionWith(entry.Value);
            }
            foreach (int id in plan.OriginalIds.Concat(new FilteredElementCollector(doc).WhereElementIsNotElementType().Cast<Element>().Where(e => e.ViewSpecific).Select(e => e.Id.IntegerValue)).Distinct())
            {
                var element = doc.GetElement(new ElementId(id));
                if (element == null) continue;
                string context = RelocationReport.Describe(element);
                var port = element as Viewport;
                int owner = port != null ? port.SheetId.IntegerValue : element is View ? id : element.OwnerViewId.IntegerValue;
                string number;
                if (sheets.TryGetValue(owner, out number)) context += "; лист № " + number;
                else
                {
                    var view = doc.GetElement(new ElementId(owner)) as View;
                    if (view != null) context += "; вид «" + view.Name + "»";
                    HashSet<string> numbers;
                    if (placed.TryGetValue(owner, out numbers)) context += "; листы № " + string.Join(", ", numbers.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
                }
                contexts[id] = context;
            }
        }
        public string IssueList() { return string.Join("\n\n", Issues.Select((text,index) => (index+1) + ". " + text)); }
    }
}
