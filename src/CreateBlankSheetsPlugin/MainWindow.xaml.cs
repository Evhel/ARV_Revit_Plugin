using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CreateBlankSheetsPlugin
{
    public partial class MainWindow : Window
    {
        public int SheetCount { get; private set; }
        public ElementId SelectedTitleBlockId { get; private set; }
        private readonly List<FamilySymbol> titleBlocks;
        public MainWindow(UIApplication app, List<FamilySymbol> types)
        {
            InitializeComponent();
            new System.Windows.Interop.WindowInteropHelper(this) { Owner = app.MainWindowHandle };
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            titleBlocks = types;
            TitleBlockComboBox.ItemsSource = types.Select(t => t.FamilyName + ": " + t.Name).ToList();
            TitleBlockComboBox.SelectedIndex = 0;
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(SheetCountTextBox.Text, out int count) || count <= 0)
            { MessageBox.Show("Введите положительное целое количество листов."); return; }
            if (TitleBlockComboBox.SelectedIndex < 0)
            { MessageBox.Show("Выберите тип основной надписи."); return; }
            SheetCount = count;
            SelectedTitleBlockId = titleBlocks[TitleBlockComboBox.SelectedIndex].Id;
            DialogResult = true;
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
        private void SheetCountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text,"^[0-9]+$"); }
        private void TitleBlockComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (titleBlocks != null && TitleBlockComboBox.SelectedIndex >= 0) SelectedTitleBlockId = titleBlocks[TitleBlockComboBox.SelectedIndex].Id; }
    }
}
