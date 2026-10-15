using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace HideCategory
{
    public partial class UserWindRoom : Window
    {
        private List<Category> categories;
        public ElementId SelectedCategoryId { get; private set; }
        public UserWindRoom(UIApplication app)
        {
            InitializeComponent();
            new System.Windows.Interop.WindowInteropHelper(this) { Owner = app.MainWindowHandle };
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            categories = app.ActiveUIDocument.Document.Settings.Categories.Cast<Category>()
                .Where(c => c.SubCategories != null && c.SubCategories.Size > 0)
                .OrderBy(c => c.Name, Algorithms.NameComparer).ToList();
            CategoryComboBox.ItemsSource = categories.Select(c => c.Name).ToList();
            CategoryComboBox.SelectedIndex = 0;
        }
        private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (categories == null || SubCategoryComboBox == null || CategoryComboBox.SelectedIndex < 0) return;
            var subs = categories[CategoryComboBox.SelectedIndex].SubCategories.Cast<Category>()
                .OrderBy(c => c.Name, Algorithms.NameComparer).ToList();
            SubCategoryComboBox.ItemsSource = subs;
            SubCategoryComboBox.DisplayMemberPath = "Name";
            SubCategoryComboBox.IsEnabled = subs.Count > 0;
            SubCategoryComboBox.SelectedIndex = 0;
        }
        private void OnHideCategoryClick(object sender, RoutedEventArgs e)
        {
            var sub = SubCategoryComboBox.SelectedItem as Category;
            if (sub == null) return;
            if (MessageBox.Show($"Скрыть подкатегорию «{sub.Name}» во всех подходящих видах и шаблонах?", "Графика ARV",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SelectedCategoryId = sub.Id;
            DialogResult = true;
        }
    }
}
