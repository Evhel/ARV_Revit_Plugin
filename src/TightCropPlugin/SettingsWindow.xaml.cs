using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.UI;
using ARV.Common;

namespace AdjustCropToBreakLines
{
    public partial class SettingsWindow : Window
    {
        public double OffsetValueMM { get; private set; }
        public bool UseBreakLinesValue { get; private set; }
        public bool UseModelElementsValue { get; private set; }
        public SettingsWindow(UIApplication app, int viewsCount)
        {
            InitializeComponent();
            new System.Windows.Interop.WindowInteropHelper(this) { Owner = app.MainWindowHandle };
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = $"Обрезка: выбрано видов {viewsCount}";
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; OkButton_Click(sender,e); }
            else if (e.Key == Key.Escape) { e.Handled = true; CancelButton_Click(sender,e); }
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Algorithms.TryNumber(OffsetTextBox.Text,out double offset))
            { TaskDialog.Show("Обрезка видов","Введите корректное конечное число для отступа."); return; }
            UseBreakLinesValue = UseBreakLinesCheckBox.IsChecked == true;
            UseModelElementsValue = UseModelElementsCheckBox.IsChecked == true;
            if (!UseBreakLinesValue && !UseModelElementsValue)
            { TaskDialog.Show("Обрезка видов","Выберите хотя бы один источник границ."); return; }
            OffsetValueMM = offset;
            DialogResult = true;
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
