using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HideCategory
{
    public partial class UserWindRoom : Window
    {
        private UIApplication uiApp;
        private Dictionary<string, List<string>> categoriesWithSubCategories;

        // Импортируем WinAPI функции для работы с окнами
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, ref RECT rectangle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public UserWindRoom(UIApplication app)
        {
            uiApp = app;
            InitializeComponent();
            LoadCategories();

            // Центрируем окно после загрузки
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
                IntPtr revitHandle = uiApp.MainWindowHandle;

                // Получаем прямоугольник окна Revit
                RECT revitRect = new RECT();
                GetWindowRect(revitHandle, ref revitRect);

                // Вычисляем центр окна Revit
                double revitCenterX = (revitRect.Left + revitRect.Right) / 2.0;
                double revitCenterY = (revitRect.Top + revitRect.Bottom) / 2.0;

                // Вычисляем позицию для нашего окна (центр относительно окна Revit)
                double windowLeft = revitCenterX - (this.Width / 2);
                double windowTop = revitCenterY - (this.Height / 2);

                // Устанавливаем позицию окна
                this.Left = windowLeft;
                this.Top = windowTop;

                // Альтернативный способ через WinAPI для более точного позиционирования
                // (раскомментируйте если нужно)
                /*
                var helper = new WindowInteropHelper(this);
                helper.EnsureHandle();
                SetWindowPos(helper.Handle, IntPtr.Zero, 
                    (int)windowLeft, (int)windowTop, 
                    (int)this.Width, (int)this.Height, 0);
                */
            }
            catch (Exception ex)
            {
                // Если не получилось центрировать - ничего страшного, окно откроется по умолчанию
                System.Diagnostics.Debug.WriteLine($"Ошибка центрирования окна: {ex.Message}");
            }
        }

        private void LoadCategories()
        {
            try
            {
                var doc = uiApp.ActiveUIDocument.Document;
                var categories = doc.Settings.Categories;
                categoriesWithSubCategories = new Dictionary<string, List<string>>();

                foreach (Category cat in categories)
                {
                    if (cat.SubCategories != null && cat.SubCategories.Cast<Category>().Any())
                    {
                        var subCategories = cat.SubCategories.Cast<Category>()
                            .Select(sc => sc.Name)
                            .OrderBy(name => name)
                            .ToList();

                        if (subCategories.Any())
                        {
                            categoriesWithSubCategories[cat.Name] = subCategories;
                        }
                    }
                }

                CategoryComboBox.ItemsSource = categoriesWithSubCategories.Keys.OrderBy(name => name).ToList();

                if (CategoryComboBox.Items.Count > 0)
                {
                    CategoryComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки категорий: {ex.Message}");
            }
        }

        private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryComboBox.SelectedItem != null)
            {
                string selectedCategory = CategoryComboBox.SelectedItem.ToString();
                if (categoriesWithSubCategories.ContainsKey(selectedCategory))
                {
                    SubCategoryComboBox.ItemsSource = categoriesWithSubCategories[selectedCategory];
                    SubCategoryComboBox.IsEnabled = true;

                    if (SubCategoryComboBox.Items.Count > 0)
                    {
                        SubCategoryComboBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    SubCategoryComboBox.ItemsSource = null;
                    SubCategoryComboBox.IsEnabled = false;
                }
            }
        }

        private void OnHideCategoryClick(object sender, RoutedEventArgs e)
        {
            if (CategoryComboBox.SelectedItem == null)
            {
                MessageBox.Show("Выберите категорию.");
                return;
            }

            if (SubCategoryComboBox.SelectedItem == null)
            {
                MessageBox.Show("Выберите подкатегорию.");
                return;
            }

            string selectedCategory = CategoryComboBox.SelectedItem.ToString();
            string selectedSubCategory = SubCategoryComboBox.SelectedItem.ToString();

            ExternalCommand.CategoryNameToHide = selectedCategory;
            ExternalCommand.SubCategoryNameToHide = selectedSubCategory;

            ExternalCommand.exEvent.Raise();
            Close();
        }
    }
}