using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ViewNumberingPlugin
{
    public enum NumberingType { Digits, Upper, Lower }

    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public static readonly char[] UpperCyrillic = "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ".ToCharArray();
        public static readonly char[] LowerCyrillic = "абвгдежзиклмнопрстуфхцчшщэюя".ToCharArray();

        // Допуск для объединения видов в одну строку (в футах, внутренние единицы Revit)
        // 0.5 фута ≈ 152 мм. Подходит для большинства стандартных листов.
        private const double RowTolerance = 0.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            var viewports = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as Viewport)
                .Where(vp => vp != null && vp.SheetId != ElementId.InvalidElementId)
                .ToList();

            if (viewports.Count == 0)
            {
                TaskDialog.Show("Ошибка", "Выберите один или несколько видов, размещённых на листе.");
                return Result.Cancelled;
            }

            var dialog = new NumberingDialog();
            if (dialog.ShowDialog() != true) return Result.Cancelled;

            int startIndex = dialog.StartIndex;
            NumberingType type = dialog.SelectedType;

            using (var tx = new Transaction(doc, "Нумерация видов на листе"))
            {
                tx.Start();
                try
                {
                    // 1. Снимаем старые номера (избегаем конфликта дубликатов)
                    for (int i = 0; i < viewports.Count; i++)
                        SetViewportDetailNumber(viewports[i], $"_TMP_{i}", doc);
                    doc.Regenerate();

                    // 2. НАДЁЖНАЯ СОРТИРОВКА ПО ПОЛОЖЕНИЮ
                    var sortedViewports = SortViewportsByPosition(viewports);

                    // 3. Установка финальных номеров
                    for (int i = 0; i < sortedViewports.Count; i++)
                    {
                        string newNumber = GenerateNumber(startIndex + i, type);
                        SetViewportDetailNumber(sortedViewports[i], newNumber, doc);
                    }

                    doc.Regenerate();
                    tx.Commit();

                    string firstNum = GenerateNumber(startIndex, type);
                    string lastNum = GenerateNumber(startIndex + sortedViewports.Count - 1, type);
                    TaskDialog.Show("Готово",
                        $"Пронумеровано видов: {sortedViewports.Count}\n" +
                        $"Логика: Строки сверху-вниз → внутри строки слева-направо\n" +
                        $"Диапазон: {firstNum} → {lastNum}");
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Ошибка", ex.Message);
                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        }

        // Явный алгоритм сортировки без зависимости от стабильности LINQ
        private List<Viewport> SortViewportsByPosition(List<Viewport> viewports)
        {
            var coords = viewports.Select(v => new
            {
                Vp = v,
                Y = v.GetBoxCenter().Y,
                X = v.GetBoxCenter().X
            }).ToList();

            // Сортируем строго по Y убывающе (сверху вниз)
            coords.Sort((a, b) => b.Y.CompareTo(a.Y));

            // Группируем в строки с учётом допуска
            var rows = new List<List<dynamic>>();
            foreach (var c in coords)
            {
                if (rows.Count == 0 || Math.Abs(c.Y - rows.Last().Last().Y) > RowTolerance)
                {
                    rows.Add(new List<dynamic> { c });
                }
                else
                {
                    rows.Last().Add(c);
                }
            }

            // Внутри каждой строки сортируем по X возрастающе (слева направо)
            foreach (var row in rows)
            {
                row.Sort((a, b) => a.X.CompareTo(b.X));
            }

            // Объединяем в один список
            return rows.SelectMany(r => r.Select(item => (Viewport)item.Vp)).ToList();
        }

        private void SetViewportDetailNumber(Viewport vp, string number, Document doc)
        {
            var param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
            if (param == null) throw new InvalidOperationException($"Вид '{vp.Name}': не найден параметр номера.");
            if (param.IsReadOnly) throw new InvalidOperationException($"Вид '{vp.Name}': параметр заблокирован (Read-Only). Проверьте шаблон вида.");
            if (!param.Set(number)) throw new InvalidOperationException($"Вид '{vp.Name}': Revit отклонил номер '{number}'.");
        }

        private string GenerateNumber(int zeroBasedIndex, NumberingType type)
        {
            if (type == NumberingType.Digits) return (zeroBasedIndex + 1).ToString();

            char[] letters = type == NumberingType.Upper ? Command.UpperCyrillic : Command.LowerCyrillic;
            if (zeroBasedIndex < letters.Length) return letters[zeroBasedIndex].ToString();

            return $"ERR_{zeroBasedIndex}"; // >32 видов для букв
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
            Width = 350; Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());

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
            var btnOk = new System.Windows.Controls.Button { Content = "Применить", Width = 90, Margin = new Thickness(5) };
            var btnCancel = new System.Windows.Controls.Button { Content = "Отмена", Width = 90, Margin = new Thickness(5) };

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
            _txtStart.Text = SelectedType == NumberingType.Digits ? "1" : "А";
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