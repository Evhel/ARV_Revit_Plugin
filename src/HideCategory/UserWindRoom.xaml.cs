using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace HideCategory
{
    public partial class UserWindRoom : Window
    {
        public ElementId SelectedCategoryId { get; private set; }
        public UserWindRoom(UIApplication app)
        {
            InitializeComponent();
            new System.Windows.Interop.WindowInteropHelper(this) { Owner = app.MainWindowHandle };
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CategoryComboBox.ItemsSource = app.ActiveUIDocument.Document.Settings.Categories.Cast<Category>()
                .Where(c => c.CategoryType == CategoryType.Model && c.IsVisibleInUI)
                .OrderBy(c => c.Name, Algorithms.NameComparer)
                .Select(c => new CategoryInfo { Id = c.Id, DisplayName = c.Name }).ToList();
            CategoryComboBox.SelectedIndex = 0;
        }
        private void OnHideCategoryClick(object sender, RoutedEventArgs e)
        {
            var category = CategoryComboBox.SelectedItem as CategoryInfo;
            if (category == null) return;
            if (MessageBox.Show($"Скрыть «{category.DisplayName}» во всех подходящих видах и шаблонах?", "Графика ARV",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SelectedCategoryId = category.Id;
            DialogResult = true;
        }
    }
    public class CategoryInfo
    {
        public ElementId Id { get; set; }
        public string DisplayName { get; set; }
    }
}
