using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CreateBlankSheetsPlugin
{
    public partial class MainWindow : Window
    {
        public int SheetCount { get; private set; }
        public ElementId SelectedTitleBlockId { get; private set; }

        private readonly List<FamilySymbol> _titleBlocks;
        private readonly UIApplication _uiApp;

        // WinAPI импорты для работы с окнами
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, ref RECT rectangle);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public MainWindow(UIApplication uiApp, List<FamilySymbol> titleBlocks)
        {
            InitializeComponent();
            _uiApp = uiApp;
            _titleBlocks = titleBlocks;

            TitleBlockComboBox.ItemsSource = _titleBlocks.Select(tb => tb.Name).ToList();
            TitleBlockComboBox.SelectedIndex = 0;
            SelectedTitleBlockId = _titleBlocks[0].Id;

            // Центрируем окно после полной загрузки
            this.Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            CenterWindowRelativeToRevit();
        }

        private void CenterWindowRelativeToRevit()
        {
            try
            {
                // Получаем главное окно Revit
                IntPtr revitHandle = _uiApp.MainWindowHandle;

                // Получаем прямоугольник окна Revit в экранных координатах
                RECT revitRect = new RECT();
                GetWindowRect(revitHandle, ref revitRect);

                // Вычисляем центр окна Revit
                double revitCenterX = (revitRect.Left + revitRect.Right) / 2.0;
                double revitCenterY = (revitRect.Top + revitRect.Bottom) / 2.0;

                // Вычисляем позицию для нашего окна (центр относительно окна Revit)
                double windowLeft = revitCenterX - (this.ActualWidth / 2);
                double windowTop = revitCenterY - (this.ActualHeight / 2);

                // Устанавливаем позицию окна
                this.Left = windowLeft;
                this.Top = windowTop;
            }
            catch
            {
                // Если не получилось — окно откроется в стандартной позиции, это не критично
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(SheetCountTextBox.Text, out int count) || count <= 0)
            {
                MessageBox.Show("Введите корректное количество листов (целое число > 0).", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TitleBlockComboBox.SelectedIndex < 0)
            {
                MessageBox.Show("Выберите тип основной надписи.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SheetCount = count;
            SelectedTitleBlockId = _titleBlocks[TitleBlockComboBox.SelectedIndex].Id;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void SheetCountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void TitleBlockComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TitleBlockComboBox.SelectedIndex >= 0)
                SelectedTitleBlockId = _titleBlocks[TitleBlockComboBox.SelectedIndex].Id;
        }
    }
}