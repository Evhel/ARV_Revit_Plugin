using System.Collections.Generic;
using System.Windows;

namespace CeilingPlugin
{
    public partial class CeilingCreatorWindow : Window
    {
        public List<CeilingTypeItem> CeilingTypes { get; set; }

        public CeilingCreatorWindow(List<StyleMapping> mappings, List<CeilingTypeItem> types)
        {
            InitializeComponent();
            CeilingTypes = types;
            DataGridCeilings.ItemsSource = mappings;
            DataContext = this;
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}