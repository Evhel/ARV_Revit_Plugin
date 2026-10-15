using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace CopyViewsWithRename
{
    public partial class CopyViewsWindow : Window
    {
        private List<View> _selectedViews;

        public int CopiesCount { get; private set; }
        public string FindText { get; private set; }
        public string ReplaceText { get; private set; }
        public bool UseReplace { get; private set; }
        public ViewDuplicateOption DuplicateOption { get; private set; }

        public CopyViewsWindow(List<View> selectedViews)
        {
            InitializeComponent();
            _selectedViews = selectedViews;
            ViewsListBox.ItemsSource = selectedViews;
            CopiesCount = 1;
            UseReplace = false;
            DuplicateOption = ViewDuplicateOption.Duplicate;

            // Подписываемся на события
            UseReplaceCheckBox.Checked += UseReplaceChecked;
            UseReplaceCheckBox.Unchecked += UseReplaceUnchecked;
            CopiesTextBox.TextChanged += (sender, args) => UpdatePreview();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void UseReplaceChecked(object sender, RoutedEventArgs e)
        {
            ReplacePanel.IsEnabled = true;
            ReplaceTextPanel.IsEnabled = true;
            UseReplace = true;
            UpdatePreview();

            // ИСПРАВЛЕНО: Используем полное имя System.Windows.Visibility
            PreviewTextBlock.Visibility = System.Windows.Visibility.Visible;
            PreviewListBox.Visibility = System.Windows.Visibility.Visible;
        }

        private void UseReplaceUnchecked(object sender, RoutedEventArgs e)
        {
            ReplacePanel.IsEnabled = false;
            ReplaceTextPanel.IsEnabled = false;
            UseReplace = false;
            UpdatePreview();

            // ИСПРАВЛЕНО: Используем полное имя System.Windows.Visibility
            PreviewTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            PreviewListBox.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void UpdatePreview()
        {
            if (PreviewListBox == null || CopiesTextBox == null || FindTextBox == null || ReplaceTextBox == null || _selectedViews == null) return;
            PreviewListBox.Items.Clear();

            if (!int.TryParse(CopiesTextBox.Text, out int copies) || copies <= 0 || copies > 100)
                return;

            string findText = FindTextBox.Text;
            string replaceText = ReplaceTextBox.Text;
            bool useReplace = UseReplace;

            foreach (View view in _selectedViews)
            {
                for (int i = 1; i <= copies; i++)
                {
                    string newName = view.Name;

                    if (useReplace && !string.IsNullOrEmpty(findText))
                    {
                        newName = newName.Replace(findText, replaceText);
                    }

                    if (copies > 1 || !useReplace)
                    {
                        newName = $"{newName}_копия{i}";
                    }
                    else
                    {
                        newName = $"{newName}_{i}";
                    }

                    PreviewListBox.Items.Add($"{view.Name} → {newName}");
                }
            }

            // Прокручиваем к последнему элементу
            if (PreviewListBox.Items.Count > 0)
                PreviewListBox.ScrollIntoView(PreviewListBox.Items[PreviewListBox.Items.Count - 1]);
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void ReplaceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверка количества копий
            if (!int.TryParse(CopiesTextBox.Text, out int copies) || copies <= 0 || copies > 100)
            {
                MessageBox.Show("Введите корректное количество копий (от 1 до 100)", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Определяем тип копирования
            if (DuplicateRadioButton.IsChecked == true)
                DuplicateOption = ViewDuplicateOption.Duplicate;
            else if (DuplicateWithDetailingRadioButton.IsChecked == true)
                DuplicateOption = ViewDuplicateOption.WithDetailing;
            else if (DuplicateAsDependentRadioButton.IsChecked == true)
                DuplicateOption = ViewDuplicateOption.AsDependent;

            CopiesCount = copies;
            FindText = FindTextBox.Text;
            ReplaceText = ReplaceTextBox.Text;

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