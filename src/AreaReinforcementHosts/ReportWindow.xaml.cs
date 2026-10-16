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
            var skipped = results.Where(r => r.Status == "Пропущено").ToList();
            SummaryText.Text = "Изменено: " + results.Count(r => r.Status == "Изменено") +
                ". Пропущено: " + skipped.Count + ".";
            ResultsGrid.ItemsSource = skipped;
            if (skipped.Count == 0)
            {
                Width = 620;
                Height = 300;
                ResultsGrid.Visibility = Visibility.Collapsed;
                ConsequenceText.Text = "Пропущенных элементов нет. ID изменились; старые марки и размеры могут потерять связь.";
            }
            else if (skipped.Count == 1)
            {
                SummaryText.Visibility = results.Count == 1 ? Visibility.Collapsed : Visibility.Visible;
                Width = 620;
                Height = results.Count == 1 ? 380 : 420;
                ResultsGrid.Visibility = Visibility.Collapsed;
                SingleResult.Visibility = Visibility.Visible;
                var result = skipped[0];
                SingleStatus.Text = result.Status;
                SingleIds.Text = "Армирование: " + result.OldId + " → " + result.NewId + "   Основа: " + result.HostId;
                SingleDetail.Text = result.Detail;
                ConsequenceText.Text = "Пропущенное армирование сохранено без изменений.";
            }
            else
            {
                ResultsGrid.Height = System.Math.Min(320, 38 + 32 * System.Math.Max(1, skipped.Count));
                Height = System.Math.Min(600, 280 + ResultsGrid.Height);
            }
        }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
