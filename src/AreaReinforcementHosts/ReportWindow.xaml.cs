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
            if (results.Count == 1)
            {
                SummaryText.Visibility = Visibility.Collapsed;
                Width = 620;
                Height = 380;
                ResultsGrid.Visibility = Visibility.Collapsed;
                SingleResult.Visibility = Visibility.Visible;
                var result = results[0];
                SingleStatus.Text = result.Status;
                SingleIds.Text = "Армирование: " + result.OldId + " → " + result.NewId + "   Основа: " + result.HostId;
                SingleDetail.Text = result.Detail;
                ConsequenceText.Text = result.Status == "Изменено"
                    ? "ID изменился. Старые марки и размеры могут потерять связь."
                    : "Исходное армирование сохранено.";
            }
            else
            {
                ResultsGrid.Height = System.Math.Min(320, 38 + 32 * System.Math.Max(1, results.Count));
                Height = System.Math.Min(600, 280 + ResultsGrid.Height);
            }
        }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
