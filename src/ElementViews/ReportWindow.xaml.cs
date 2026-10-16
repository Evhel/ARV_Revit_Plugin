using System;
using System.Linq;
using System.Windows;
using ARV.Common;

namespace ARV.ElementViews
{
    public partial class ReportWindow : Window
    {
        public ReportWindow(CreationResult result)
        {
            InitializeComponent();UiSupport.Initialize(this);
            ContextText.Text="Комплекты создаются отдельно для каждого отобранного элемента.";
            SummaryText.Text="Обработано: "+result.Completed+". Виды: "+result.Views+". Листы: "+result.Sheets+". Не создано: "+result.Skipped.Count+". Требуют внимания: "+result.Warnings.Count+".\nИсключено отбором: "+result.Filtered+", из них пустых: "+result.EmptyExcluded+".";
            SkippedGrid.ItemsSource=result.Skipped.Concat(result.Warnings).ToList();
            if(result.Skipped.Count+result.Warnings.Count==0) {SkippedGrid.Visibility=Visibility.Collapsed;Width=620;Height=300;}
            else {SkippedGrid.Height=Math.Min(260,50+(result.Skipped.Count+result.Warnings.Count)*55);Height=Math.Min(550,280+SkippedGrid.Height);}
        }
        private void Ok_Click(object sender,RoutedEventArgs e) {Close();}
        private void Cancel_Click(object sender,RoutedEventArgs e) {Close();}
    }
}
