using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace PlaceViewsOnSheet
{
    public partial class MainWindow : Window
    {
        private List<View> views;
        private List<ViewSheet> sheets;

        public ViewSheet SelectedSheet { get; private set; }
        public double StepX { get; private set; } // в миллиметрах
        public double StepY { get; private set; } // в миллиметрах
        public int ViewsPerRow { get; private set; } // количество видов в ряду

        public MainWindow(List<View> selectedViews, List<ViewSheet> availableSheets)
        {
            InitializeComponent();

            views = selectedViews;
            sheets = availableSheets;

            // Заполняем список видов
            ViewsListBox.ItemsSource = views;

            // Заполняем список листов
            SheetsComboBox.ItemsSource = sheets;
            if (sheets.Any())
                SheetsComboBox.SelectedIndex = 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем выбран ли лист
            if (SheetsComboBox.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, выберите лист.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Парсим количество видов в ряду
            if (!int.TryParse(ViewsPerRowTextBox.Text, out int viewsPerRow) || viewsPerRow <= 0)
            {
                MessageBox.Show("Введите корректное количество видов в ряду (положительное целое число).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Парсим значения шага (в миллиметрах)
            if (!ARV.Common.Algorithms.TryNumber(StepXTextBox.Text, out double stepX) || stepX <= 0)
            {
                MessageBox.Show("Введите корректное значение шага по X (положительное число в миллиметрах).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ARV.Common.Algorithms.TryNumber(StepYTextBox.Text, out double stepY) || stepY <= 0)
            {
                MessageBox.Show("Введите корректное значение шага по Y (положительное число в миллиметрах).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedSheet = (ViewSheet)SheetsComboBox.SelectedItem;
            StepX = stepX;
            StepY = stepY;
            ViewsPerRow = viewsPerRow;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}