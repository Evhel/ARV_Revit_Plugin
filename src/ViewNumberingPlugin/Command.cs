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
    public class NumberingDialog : Window
    {
        public int StartIndex { get; private set; }
        public NumberingType SelectedType { get; private set; }

        private System.Windows.Controls.ComboBox _cmbType;
        private System.Windows.Controls.TextBox _txtStart;

        public NumberingDialog()
        {
            Title = "Нумерация видов на листе";
            Width = 380; SizeToContent = SizeToContent.Height; MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lblType = new System.Windows.Controls.Label { Content = "Тип нумерации:", Margin = new Thickness(0, 0, 0, 2) };
            System.Windows.Controls.Grid.SetRow(lblType, 0); grid.Children.Add(lblType);

            _cmbType = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
            _cmbType.Items.Add("Цифры (1, 2, 3...)");
            _cmbType.Items.Add("Заглавные (А, Б, В...)");
            _cmbType.Items.Add("Строчные (а, б, в...)");
            _cmbType.SelectedIndex = 0;
            _cmbType.SelectionChanged += (s, e) => UpdateInfo();
            System.Windows.Controls.Grid.SetRow(_cmbType, 1); grid.Children.Add(_cmbType);

            var lblStart = new System.Windows.Controls.Label { Content = "Начать с:", Margin = new Thickness(0, 0, 0, 2) };
            System.Windows.Controls.Grid.SetRow(lblStart, 2); grid.Children.Add(lblStart);

            _txtStart = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center, Text = "1" };
            System.Windows.Controls.Grid.SetRow(_txtStart, 3); grid.Children.Add(_txtStart);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new System.Windows.Controls.Button { Content = "Применить", IsDefault = true, Width = 100, Margin = new Thickness(5) };
            var btnCancel = new System.Windows.Controls.Button { Content = "Отмена", IsCancel = true, Width = 90, Margin = new Thickness(5) };

            btnOk.Click += (s, e) => { if (ValidateInput()) DialogResult = true; };
            btnCancel.Click += (s, e) => DialogResult = false;

            btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnCancel);
            System.Windows.Controls.Grid.SetRow(btnPanel, 4); grid.Children.Add(btnPanel);

            Content = grid; UpdateInfo();
            _txtStart.Focus(); _txtStart.SelectAll();
            _txtStart.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter && ValidateInput()) DialogResult = true; };
        }

        private void UpdateInfo()
        {
            SelectedType = (NumberingType)_cmbType.SelectedIndex;
            _txtStart.Text = SelectedType == NumberingType.Digits ? "1" : SelectedType == NumberingType.Upper ? "А" : "а";
        }

        private bool ValidateInput()
        {
            SelectedType = (NumberingType)_cmbType.SelectedIndex;
            var text = _txtStart.Text.Trim();

            if (SelectedType == NumberingType.Digits)
            {
                if (int.TryParse(text, out int num) && num > 0) { StartIndex = num - 1; return true; }
                MessageBox.Show("Введите целое положительное число.");
                return false;
            }

            if (SelectedType == NumberingType.Upper)
            {
                if (text.Length == 1 && Command.UpperCyrillic.Contains(char.ToUpper(text[0])))
                { StartIndex = Array.IndexOf(Command.UpperCyrillic, char.ToUpper(text[0])); return true; }
                MessageBox.Show("Введите одну заглавную букву (А-Я).");
                return false;
            }

            if (SelectedType == NumberingType.Lower)
            {
                if (text.Length == 1 && Command.LowerCyrillic.Contains(char.ToLower(text[0])))
                { StartIndex = Array.IndexOf(Command.LowerCyrillic, char.ToLower(text[0])); return true; }
                MessageBox.Show("Введите одну строчную букву (а-я).");
                return false;
            }
            return false;
        }
    }
}