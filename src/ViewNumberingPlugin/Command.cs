using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace ViewNumberingPlugin
{
    public enum NumberingType { Digits, Upper, Lower }

    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public static readonly char[] UpperCyrillic = "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ".ToCharArray();
        public static readonly char[] LowerCyrillic = "абвгдежзиклмнопрстуфхцчшщэюя".ToCharArray();
        private const double RowTolerance = 10.0 / 304.8; // 10 мм на листе, без цепочного объединения строк.

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                var selected = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<Viewport>()
                    .Where(v => v.SheetId != ElementId.InvalidElementId).ToList();
                if (selected.Count == 0)
                {
                    TaskDialog.Show("Нумерация видов", "Выберите видовые экраны на листе.");
                    return Result.Cancelled;
                }
                var dialog = new NumberingDialog();
                new System.Windows.Interop.WindowInteropHelper(dialog) { Owner = data.Application.MainWindowHandle };
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() != true) return Result.Cancelled;
                var changes = new List<Tuple<Viewport, string>>();
                foreach (var sheetGroup in selected.GroupBy(v => v.SheetId.IntegerValue))
                {
                    var sorted = SortViewportsByPosition(sheetGroup.ToList());
                    var sheet = (ViewSheet)doc.GetElement(new ElementId(sheetGroup.Key));
                    var selectedIds = new HashSet<int>(sorted.Select(v => v.Id.IntegerValue));
                    var occupied = new HashSet<string>(sheet.GetAllViewports().Where(id => !selectedIds.Contains(id.IntegerValue))
                        .Select(id => RevitSupport.Text(doc.GetElement(id).get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER))), StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        string number = GenerateNumber(checked(dialog.StartIndex + i), dialog.SelectedType);
                        if (occupied.Contains(number)) throw new InvalidOperationException($"Лист {sheet.SheetNumber}: номер «{number}» занят невыбранным видом. Изменения не внесены.");
                        changes.Add(Tuple.Create(sorted[i], number));
                    }
                }
                using (var transaction = new Transaction(doc, "Нумерация видов на листе"))
                {
                    transaction.Start();
                    // Случайный префикс + проверка занятых номеров не затрагивают невыбранные виды.
                    var occupied = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                        .Select(v => RevitSupport.Text(v.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER))), StringComparer.OrdinalIgnoreCase);
                    foreach (var change in changes)
                        SetNumber(change.Item1, Algorithms.UniqueName("ARV_" + Guid.NewGuid().ToString("N"), occupied));
                    doc.Regenerate();
                    foreach (var change in changes) SetNumber(change.Item1, change.Item2);
                    doc.Regenerate();
                    RevitSupport.Commit(transaction);
                }
                TaskDialog.Show("Нумерация видов", $"Пронумеровано: {changes.Count}. На каждом листе: сверху вниз, слева направо.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        private static List<Viewport> SortViewportsByPosition(List<Viewport> viewports)
        {
            var remaining = viewports.OrderByDescending(v => v.GetBoxCenter().Y).ThenBy(v => v.Id.IntegerValue).ToList();
            var result = new List<Viewport>();
            while (remaining.Count > 0)
            {
                double rowY = remaining[0].GetBoxCenter().Y;
                var row = remaining.Where(v => rowY - v.GetBoxCenter().Y <= RowTolerance)
                    .OrderBy(v => v.GetBoxCenter().X).ThenBy(v => v.Id.IntegerValue).ToList();
                result.AddRange(row);
                foreach (var item in row) remaining.Remove(item);
            }
            return result;
        }

        private static void SetNumber(Viewport viewport, string value)
        {
            var parameter = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
            if (parameter == null || parameter.IsReadOnly) throw new InvalidOperationException("Номер видового экрана недоступен: " + viewport.Id);
            if (parameter.AsString() != value && !parameter.Set(value)) throw new InvalidOperationException("Не удалось записать номер «" + value + "».");
        }

        public static string GenerateNumber(int index, NumberingType type)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (type == NumberingType.Digits) return ((long)index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Algorithms.LetterNumber(index, new string(type == NumberingType.Upper ? UpperCyrillic : LowerCyrillic));
        }
    }
}
