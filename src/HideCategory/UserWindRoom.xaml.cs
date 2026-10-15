using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HideCategory
{
    public partial class UserWindRoom : Window
    {
        private UIApplication _uiApp;

        public UserWindRoom(UIApplication uiApp)
        {
            InitializeComponent();
            _uiApp = uiApp;

            // Центрируем окно
            var revitWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (revitWindow != IntPtr.Zero)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = revitWindow;
            }

            LoadCategories();
        }

        private void LoadCategories()
        {
            try
            {
                var doc = _uiApp.ActiveUIDocument.Document;
                var categories = doc.Settings.Categories;

                var categoryList = new List<CategoryInfo>();

                // Берем только категории типа Model (3D модели)
                foreach (Category cat in categories)
                {
                    if (cat.CategoryType == CategoryType.Model && cat.IsVisibleInUI)
                    {
                        categoryList.Add(new CategoryInfo
                        {
                            Id = cat.Id,
                            DisplayName = cat.Name
                        });
                    }
                }

                // Сортируем по алфавиту
                categoryList = categoryList.OrderBy(c => c.DisplayName).ToList();

                CategoryComboBox.ItemsSource = categoryList;

                if (categoryList.Any())
                    CategoryComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnHideCategoryClick(object sender, RoutedEventArgs e)
        {
            if (CategoryComboBox.SelectedItem == null)
                return;

            var selectedCategory = (CategoryInfo)CategoryComboBox.SelectedItem;

            var result = MessageBox.Show($"Скрыть категорию \"{selectedCategory.DisplayName}\" во всех видах?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                ProgressBar.Visibility = System.Windows.Visibility.Visible;
                CategoryComboBox.IsEnabled = false;
                ((Button)sender).IsEnabled = false;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    ExternalCommand.CategoryIdToHide = selectedCategory.Id;
                    ExternalCommand.exEvent.Raise();
                });

                await System.Threading.Tasks.Task.Delay(500);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressBar.Visibility = System.Windows.Visibility.Collapsed;
                CategoryComboBox.IsEnabled = true;
                ((Button)sender).IsEnabled = true;
            }
        }
    }

    public class CategoryInfo
    {
        public ElementId Id { get; set; }
        public string DisplayName { get; set; }
    }
}