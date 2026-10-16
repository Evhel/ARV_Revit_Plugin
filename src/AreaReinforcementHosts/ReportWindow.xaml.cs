using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ARV.AreaReinforcementHosts
{
    public partial class ReportWindow : Window
    {
        public ReportWindow(IList<RehostResult> results, string context)
        {
            InitializeComponent();
            ARV.Common.UiSupport.Initialize(this);
            ContextText.Text = context;
            SummaryText.Text = "Изменено: " + results.Count(r => r.Status == "Изменено") +
                ". Без изменений: " + results.Count(r => r.Status == "Без изменений") +
                ". Пропущено: " + results.Count(r => r.Status == "Пропущено") + ".";
            ResultsGrid.ItemsSource = results;
        }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
