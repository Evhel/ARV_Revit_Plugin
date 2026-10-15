using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace FinishingSchedulePlugin
{
    public partial class MainWindow : Window
    {
        public double Factor { get; private set; }
        public string WallGroupFilter { get; private set; }
        public string CeilingGroupFilter { get; private set; }
        public bool IsCancelled { get; private set; } = true;

        public MainWindow(UIDocument uiDoc)
        {
            InitializeComponent();

            // Устанавливаем владельца окна
            var helper = new WindowInteropHelper(this);
            helper.Owner = uiDoc.Application.MainWindowHandle;

            // Центрируем окно на экране где находится Revit
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            PopulateComboBoxes(uiDoc.Document);
        }

        private void PopulateComboBoxes(Document doc)
        {
            List<string> wallValues = GetTypeParameterValues(doc, BuiltInCategory.OST_Walls);
            List<string> ceilingValues = GetTypeParameterValues(doc, BuiltInCategory.OST_Ceilings);

            cmbWalls.Items.Add("Нет");
            foreach (var value in wallValues)
            {
                cmbWalls.Items.Add(value);
            }
            cmbWalls.SelectedIndex = 0;

            cmbCeilings.Items.Add("Нет");
            foreach (var value in ceilingValues)
            {
                cmbCeilings.Items.Add(value);
            }
            cmbCeilings.SelectedIndex = 0;
        }

        private List<string> GetTypeParameterValues(Document doc, BuiltInCategory category)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            var values = new HashSet<string>();

            foreach (Element elem in collector)
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId == ElementId.InvalidElementId)
                    continue;

                ElementType elemType = doc.GetElement(typeId) as ElementType;
                if (elemType == null)
                    continue;

                Parameter param = elemType.LookupParameter("Группа модели") ??
                                  elemType.LookupParameter("Группа") ??
                                  elemType.LookupParameter("Model Group");

                if (param != null)
                {
                    string val = ARV.Common.RevitSupport.Text(param);
                    if (!string.IsNullOrEmpty(val))
                    {
                        values.Add(val);
                    }
                }
            }
            return values.OrderBy(v => v, ARV.Common.Algorithms.NameComparer).ToList();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!ARV.Common.Algorithms.TryNumber(txtFactor.Text, out double factor))
            {
                MessageBox.Show("Введите корректное число для коэффициента.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (factor < 0 || factor > 100)
            {
                MessageBox.Show("Коэффициент должен быть от 0 до 100.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Factor = factor;
            WallGroupFilter = cmbWalls.SelectedItem?.ToString() ?? "Нет";
            CeilingGroupFilter = cmbCeilings.SelectedItem?.ToString() ?? "Нет";

            IsCancelled = false;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsCancelled = true;
            this.DialogResult = false;
            this.Close();
        }
    }
}