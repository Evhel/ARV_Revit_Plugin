using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MyPlugin
{
    public partial class MainWindow : Window
    {
        private Document doc;
        private List<Family> allFamilies;
        private List<FamilyItem> familyItems;
        private Level level;
        private bool isProcessing = false;

        // Константа для перевода мм в футы
        private const double MM_TO_FEET = 304.8;

        public MainWindow(Document document)
        {
            InitializeComponent();
            doc = document;
            LoadCategories();
            level = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstOrDefault() as Level;
            if (level == null)
                MessageBox.Show("В проекте нет уровней!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);

            textBoxCountX.TextChanged += (s, e) => UpdateAutoCountY();
            listBoxFamilies.SelectionChanged += (s, e) => UpdateAutoCountY();

            // Значения по умолчанию в мм
            textBoxStepX.Text = "1000";
            textBoxStepY.Text = "1000";
            textBoxCountX.Text = "5";
        }

        private void UpdateAutoCountY()
        {
            try
            {
                if (isProcessing) return;

                int countX = 5;
                int.TryParse(textBoxCountX.Text, out countX);

                int selectedCount = familyItems?.Count(f => f.IsSelected) ?? 0;

                if (familyItems != null)
                {
                    int totalCount = familyItems.Count;
                    lblStatus.Content = $"Выбрано: {selectedCount} из {totalCount} семейств";
                }

                if (selectedCount == 0 || countX == 0)
                {
                    lblCountY.Content = "0";
                    return;
                }

                int countY = (int)Math.Ceiling((double)selectedCount / countX);
                lblCountY.Content = countY.ToString();
            }
            catch
            {
                lblCountY.Content = "0";
            }
        }

        private void LoadCategories()
        {
            try
            {
                lblStatus.Content = "Загрузка категорий...";
                progressBar.Visibility = System.Windows.Visibility.Visible;
                progressBar.IsIndeterminate = true;
                this.IsEnabled = false;

                allFamilies = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();

                var categories = allFamilies
                    .Select(f => f.FamilyCategory)
                    .Where(c => c != null)
                    .GroupBy(c => c.Id)
                    .Select(g => g.First())
                    .OrderBy(c => c.Name)
                    .ToList();

                comboBoxCategories.ItemsSource = categories;
                comboBoxCategories.DisplayMemberPath = "Name";
                if (categories.Any())
                    comboBoxCategories.SelectedIndex = 0;

                lblStatus.Content = "Готово";
                progressBar.Visibility = System.Windows.Visibility.Collapsed;
                this.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки категорий: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                this.IsEnabled = true;
                progressBar.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void ComboBoxCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedCategory = comboBoxCategories.SelectedItem as Category;
            if (selectedCategory == null) return;

            try
            {
                lblStatus.Content = "Загрузка семейств...";
                progressBar.Visibility = System.Windows.Visibility.Visible;
                progressBar.IsIndeterminate = true;
                this.IsEnabled = false;

                var familiesInCategory = allFamilies
                    .Where(f => f.FamilyCategory != null && f.FamilyCategory.Id == selectedCategory.Id)
                    .OrderBy(f => f.Name)
                    .ToList();

                familyItems = familiesInCategory.Select(f => new FamilyItem { Family = f, IsSelected = false }).ToList();
                listBoxFamilies.ItemsSource = familyItems;

                progressBar.Visibility = System.Windows.Visibility.Collapsed;
                this.IsEnabled = true;
                UpdateAutoCountY();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки семейств: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                this.IsEnabled = true;
                progressBar.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (familyItems == null) return;
            foreach (var item in familyItems)
                item.IsSelected = true;
            UpdateAutoCountY();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (familyItems == null) return;
            foreach (var item in familyItems)
                item.IsSelected = false;
            UpdateAutoCountY();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (isProcessing)
            {
                MessageBox.Show("Идет выполнение операции. Пожалуйста, подождите.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (level == null)
            {
                MessageBox.Show("Не найден уровень для размещения!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var selectedItems = familyItems?.Where(f => f.IsSelected).ToList();
            if (selectedItems == null || !selectedItems.Any())
            {
                MessageBox.Show("Выберите хотя бы одно семейство!", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Чтение параметров в МИЛЛИМЕТРАХ
            if (!double.TryParse(textBoxStepX.Text, out double stepX_mm) || stepX_mm <= 0)
            {
                MessageBox.Show("Введите положительное число для шага X (в мм).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!double.TryParse(textBoxStepY.Text, out double stepY_mm) || stepY_mm <= 0)
            {
                MessageBox.Show("Введите положительное число для шага Y (в мм).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!int.TryParse(textBoxCountX.Text, out int countX) || countX <= 0)
            {
                MessageBox.Show("Введите положительное целое число для количества по X.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // КОНВЕРТАЦИЯ мм → футы (Revit работает в футах)
            double stepX = stepX_mm / MM_TO_FEET;
            double stepY = stepY_mm / MM_TO_FEET;

            int totalElements = selectedItems.Count;
            int countY = (int)Math.Ceiling((double)totalElements / countX);

            isProcessing = true;
            this.IsEnabled = false;
            progressBar.Visibility = System.Windows.Visibility.Visible;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;
            lblStatus.Content = $"Размещение {totalElements} элементов (сетка {countX}x{countY})...";

            var familiesToPlace = selectedItems.Select(f => f.Family).ToList();
            var uiDispatcher = this.Dispatcher;

            System.Threading.Tasks.Task.Run(() =>
            {
                int counter = 0;
                Exception error = null;

                uiDispatcher.Invoke(new Action(() =>
                {
                    using (Transaction trans = new Transaction(doc, "Размещение семейств"))
                    {
                        try
                        {
                            trans.Start();

                            for (int f = 0; f < familiesToPlace.Count; f++)
                            {
                                var family = familiesToPlace[f];

                                var symbolIds = family.GetFamilySymbolIds();
                                if (symbolIds == null || symbolIds.Count == 0)
                                    continue;

                                var firstSymbolId = symbolIds.First();
                                var symbol = doc.GetElement(firstSymbolId) as FamilySymbol;
                                if (symbol == null) continue;

                                if (!symbol.IsActive)
                                    symbol.Activate();

                                int row = f / countX;
                                int col = f % countX;

                                double xOffset = col * stepX;
                                double yOffset = row * stepY;

                                XYZ location = new XYZ(xOffset, yOffset, 0);
                                doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.NonStructural);

                                counter++;

                                int progress = (int)((double)counter / totalElements * 100);
                                progressBar.Value = progress;
                                lblStatus.Content = $"Размещение: {progress}% ({counter} из {totalElements})";
                            }

                            trans.Commit();
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                            trans.RollBack();
                        }
                    }
                }));

                uiDispatcher.Invoke(new Action(() =>
                {
                    if (error == null)
                    {
                        MessageBox.Show($"Успешно размещено {counter} элементов.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Ошибка при размещении: {error.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    progressBar.Visibility = System.Windows.Visibility.Collapsed;
                    this.IsEnabled = true;
                    isProcessing = false;
                    lblStatus.Content = error == null ? $"Готово. Размещено {counter} элементов." : "Ошибка";
                    UpdateAutoCountY();
                }));
            });
        }
    }

    public class FamilyItem : INotifyPropertyChanged
    {
        public Family Family { get; set; }
        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        public string Name => Family?.Name ?? "Без имени";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}