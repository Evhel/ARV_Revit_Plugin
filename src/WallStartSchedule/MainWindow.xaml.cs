using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WallStartSchedule
{
    public partial class MainWindow : Window
    {
        private Document _doc;
        private List<ElementType> _allWallTypes;
        private List<ElementType> _filteredWallTypes;
        private List<string> _groupValues;

        public Dictionary<int, string> SelectedFinishTypes { get; private set; }
        public Dictionary<int, string> ColumnHeaderNames { get; private set; }
        public double AreaCoefficient { get; private set; }

        public MainWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;
            SelectedFinishTypes = new Dictionary<int, string>();
            ColumnHeaderNames = new Dictionary<int, string>();
            _groupValues = new List<string>();
            AreaCoefficient = 0;

            LoadWallTypes();
            LoadGroupValues();
            InitializeFilterComboBox();
            InitializeComboBoxes();

            // Центрируем окно относительно Revit
            CenterWindowOnRevit();
        }

        private void CenterWindowOnRevit()
        {
            try
            {
                // Получаем HWND активного окна Revit
                var revitWindow = System.Diagnostics.Process.GetProcessesByName("Revit")
                    .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

                if (revitWindow != null && revitWindow.MainWindowHandle != IntPtr.Zero)
                {
                    // Получаем размеры и позицию окна Revit
                    var revitRect = GetWindowRect(revitWindow.MainWindowHandle);

                    // Получаем размеры нашего окна
                    this.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double windowWidth = this.Width;
                    double windowHeight = this.Height;

                    // Если размеры не заданы явно, используем стандартные
                    if (windowWidth <= 0) windowWidth = 800;
                    if (windowHeight <= 0) windowHeight = 600;

                    // Вычисляем позицию для центрирования относительно окна Revit
                    double left = revitRect.Left + (revitRect.Width - windowWidth) / 2;
                    double top = revitRect.Top + (revitRect.Height - windowHeight) / 2;

                    // Устанавливаем позицию окна
                    this.Left = left;
                    this.Top = top;
                }
                else
                {
                    // Если не нашли окно Revit, центрируем на экране по умолчанию
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch
            {
                // В случае ошибки центрируем на экране по умолчанию
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private Rect GetWindowRect(IntPtr hwnd)
        {
            var rect = new Rect();
            NativeMethods.GetWindowRect(hwnd, ref rect);
            return rect;
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, ref Rect lpRect);
        }

        private void LoadWallTypes()
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                collector.OfCategory(BuiltInCategory.OST_Walls);
                collector.WhereElementIsElementType();

                _allWallTypes = collector.Cast<ElementType>()
                    .Where(w => w.Name != null && !string.IsNullOrEmpty(w.Name))
                    .OrderBy(w => w.Name)
                    .ToList();

                _filteredWallTypes = new List<ElementType>(_allWallTypes);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _allWallTypes = new List<ElementType>();
                _filteredWallTypes = new List<ElementType>();
            }
        }

        private void LoadGroupValues()
        {
            try
            {
                HashSet<string> groups = new HashSet<string>();

                foreach (var wallType in _allWallTypes)
                {
                    Parameter groupParam = wallType.LookupParameter("Группа модели");

                    if (groupParam == null || !groupParam.HasValue)
                    {
                        groupParam = wallType.LookupParameter("Model Group");
                    }

                    if (groupParam != null && groupParam.HasValue)
                    {
                        string groupName = groupParam.AsString();
                        if (!string.IsNullOrEmpty(groupName))
                        {
                            groups.Add(groupName);
                        }
                    }
                }

                _groupValues = groups.OrderBy(g => g).ToList();
            }
            catch
            {
                _groupValues = new List<string>();
            }
        }

        private void InitializeFilterComboBox()
        {
            FilterComboBox.Items.Clear();

            FilterComboBox.Items.Add(new ComboBoxItem { Content = "— Все —", Tag = null });

            foreach (var group in _groupValues)
            {
                FilterComboBox.Items.Add(new ComboBoxItem { Content = group, Tag = group });
            }

            FilterComboBox.SelectedIndex = 0;
        }

        private void InitializeComboBoxes()
        {
            var comboBoxes = new[] { ComboBox1, ComboBox2, ComboBox3, ComboBox4, ComboBox5 };

            foreach (var comboBox in comboBoxes)
            {
                comboBox.Items.Clear();
                comboBox.Items.Add(new ComboBoxItem { Content = "— Нет —", Tag = null });

                foreach (var wallType in _filteredWallTypes)
                {
                    var item = new ComboBoxItem { Content = wallType.Name, Tag = wallType };
                    comboBox.Items.Add(item);
                }

                comboBox.SelectedIndex = 0;
            }
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = FilterComboBox.SelectedItem as ComboBoxItem;

            if (selectedItem == null || selectedItem.Tag == null)
            {
                _filteredWallTypes = new List<ElementType>(_allWallTypes);
            }
            else
            {
                string selectedGroup = selectedItem.Tag.ToString();
                _filteredWallTypes = _allWallTypes.Where(w =>
                {
                    try
                    {
                        Parameter groupParam = w.LookupParameter("Группа модели");

                        if (groupParam == null || !groupParam.HasValue)
                        {
                            groupParam = w.LookupParameter("Model Group");
                        }

                        if (groupParam != null && groupParam.HasValue)
                        {
                            string groupName = groupParam.AsString();
                            return groupName == selectedGroup;
                        }
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                }).ToList();
            }

            var comboBoxes = new[] { ComboBox1, ComboBox2, ComboBox3, ComboBox4, ComboBox5 };
            var currentSelections = comboBoxes
                .Select(cb => cb.SelectedItem != null ? ((ComboBoxItem)cb.SelectedItem).Content?.ToString() : null)
                .ToArray();

            InitializeComboBoxes();

            for (int i = 0; i < comboBoxes.Length; i++)
            {
                if (currentSelections[i] != null)
                {
                    foreach (ComboBoxItem item in comboBoxes[i].Items)
                    {
                        if (item.Content?.ToString() == currentSelections[i])
                        {
                            comboBoxes[i].SelectedItem = item;
                            break;
                        }
                    }
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Валидация коэффициента
            if (!double.TryParse(CoefficientTextBox.Text, out double coefficient))
            {
                MessageBox.Show("Введите число от 0 до 100 для коэффициента запаса!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (coefficient < 0 || coefficient > 100)
            {
                MessageBox.Show("Коэффициент должен быть от 0 до 100!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AreaCoefficient = coefficient;

            var comboBoxes = new[] { ComboBox1, ComboBox2, ComboBox3, ComboBox4, ComboBox5 };
            int activeColumns = 0;

            SelectedFinishTypes.Clear();
            ColumnHeaderNames.Clear();

            for (int i = 0; i < comboBoxes.Length; i++)
            {
                var selectedItem = comboBoxes[i].SelectedItem as ComboBoxItem;

                if (selectedItem != null && selectedItem.Tag != null)
                {
                    var wallType = selectedItem.Tag as ElementType;
                    if (wallType != null)
                    {
                        SelectedFinishTypes[i + 1] = wallType.Name;
                        ColumnHeaderNames[i + 1] = wallType.Name;
                        activeColumns++;
                    }
                }
            }

            if (activeColumns == 0)
            {
                MessageBox.Show("Выберите хотя бы один тип!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}