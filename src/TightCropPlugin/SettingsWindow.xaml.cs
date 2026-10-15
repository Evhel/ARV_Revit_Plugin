using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Forms;
using Autodesk.Revit.UI;

namespace AdjustCropToBreakLines
{
    public partial class SettingsWindow : Window
    {
        public double OffsetValueMM { get; private set; }
        public bool UseBreakLinesValue { get; private set; }
        public bool UseModelElementsValue { get; private set; }

        public SettingsWindow(UIApplication uiapp, int viewsCount)
        {
            InitializeComponent();

            // Центрируем окно относительно экрана с Revit
            try
            {
                IntPtr revitHandle = uiapp.MainWindowHandle;
                var screen = Screen.FromHandle(revitHandle);
                var bounds = screen.WorkingArea;

                this.Left = bounds.Left + (bounds.Width - this.Width) / 2;
                this.Top = bounds.Top + (bounds.Height - this.Height) / 2;
            }
            catch
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(sender, e);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Парсим значение отступа
            if (!double.TryParse(OffsetTextBox.Text, out double offset))
            {
                TaskDialog.Show("Ошибка", "Введите корректное числовое значение отступа");
                return;
            }

            OffsetValueMM = offset;
            UseBreakLinesValue = UseBreakLinesCheckBox.IsChecked == true;
            UseModelElementsValue = UseModelElementsCheckBox.IsChecked == true;

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