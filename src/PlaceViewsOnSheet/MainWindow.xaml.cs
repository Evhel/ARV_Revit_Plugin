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
            ARV.Common.UiSupport.Initialize(this);
            ContextText.Text = $"Выбрано видов: {selectedViews.Count}. Размещение по имени вида.";

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
                ARV.Common.UiSupport.Error(this, SheetsComboBox, "Пожалуйста, выберите лист.");
                return;
            }

            // Парсим количество видов в ряду
            if (!int.TryParse(ViewsPerRowTextBox.Text, out int viewsPerRow) || viewsPerRow <= 0)
            {
                ARV.Common.UiSupport.Error(this, ViewsPerRowTextBox, "Введите корректное количество видов в ряду (положительное целое число).");
                return;
            }

            // Парсим значения шага (в миллиметрах)
            if (!ARV.Common.Algorithms.TryNumber(StepXTextBox.Text, out double stepX) || stepX <= 0)
            {
                ARV.Common.UiSupport.Error(this, StepXTextBox, "Введите корректное значение шага по X (положительное число в миллиметрах).");
                return;
            }

            if (!ARV.Common.Algorithms.TryNumber(StepYTextBox.Text, out double stepY) || stepY <= 0)
            {
                ARV.Common.UiSupport.Error(this, StepYTextBox, "Введите корректное значение шага по Y (положительное число в миллиметрах).");
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