using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ARV.Common;

namespace ARV.ElementViews
{
    public partial class MainWindow : Window
    {
        private readonly Catalog catalog;
        public List<ViewRow> Rows {get;}=new List<ViewRow>();
        public Options Settings {get;private set;}
        public MainWindow(Catalog source)
        {
            catalog=source??throw new ArgumentNullException(nameof(source));
            InitializeComponent();UiSupport.Initialize(this);
            ContextText.Text="Выбрано элементов модели: "+catalog.Elements.Count+". Неподходящих объектов: "+catalog.UnsupportedCount+".";
            foreach(var row in new[]{
                new ViewRow{Kind="Plan",Label="План 1",Enabled=true,OffsetText="1000",Prefix="План1_",Suffix=""},
                new ViewRow{Kind="Plan",Label="План 2",OffsetText="2000",Prefix="План2_",Suffix=""},
                new ViewRow{Kind="Plan",Label="План 3",OffsetText="3000",Prefix="План3_",Suffix=""},
                new ViewRow{Kind="Front",Label="Спереди",Enabled=true,OffsetText="—",Prefix="Спереди_",Suffix=""},
                new ViewRow{Kind="Back",Label="Сзади",OffsetText="—",Prefix="Сзади_",Suffix=""},
                new ViewRow{Kind="Left",Label="Слева",OffsetText="—",Prefix="Слева_",Suffix=""},
                new ViewRow{Kind="Right",Label="Справа",Enabled=true,OffsetText="—",Prefix="Справа_",Suffix=""}})
            {row.Templates=catalog.Templates;row.Template=catalog.Templates.FirstOrDefault();Rows.Add(row);}
            ViewsGrid.ItemsSource=Rows;
            ParameterComboBox.ItemsSource=catalog.Parameters;ParameterComboBox.SelectedIndex=0;
            SectionTypeComboBox.ItemsSource=catalog.SectionTypes;SectionTypeComboBox.SelectedIndex=0;
            TitleBlockComboBox.ItemsSource=catalog.TitleBlocks;TitleBlockComboBox.SelectedIndex=catalog.TitleBlocks.Count>0?0:-1;
            if(catalog.TitleBlocks.Count==0) {CreateSheetCheckBox.IsEnabled=false;SheetHelp.Text="Основные надписи не загружены: можно создать виды без листов.";}
            int fallback=catalog.Elements.Count(item=>item.OrientationNote!=null && item.OrientationNote.IndexOf("оси проекта",StringComparison.OrdinalIgnoreCase)>=0);
            if(fallback>0) ContextText.Text+=" Для "+fallback+" элементов без локальных осей применяются оси проекта.";
            RefreshPreview();
        }
        private void RefreshPreview()
        {
            if(catalog==null || ParameterComboBox==null || CountInfo==null || SheetNamePreview==null) return;
            var parameter=ParameterComboBox.SelectedItem as Choice;
            var selected=catalog.Select(parameter?.Key,UniqueCheckBox.IsChecked==true);
            CountInfo.Text="К обработке: "+selected.Count+" из "+catalog.Elements.Count+". Повторы исключены: "+(catalog.Elements.Count-selected.Count)+".";
            string token=selected.Count==0 || string.IsNullOrWhiteSpace(selected[0].Value)?"1":selected[0].Value.Trim();
            foreach(var row in Rows) row.Token=token;
            try {SheetNamePreview.Text="Пример: "+ElementViewMath.Name(SheetPrefixTextBox.Text,token,SheetSuffixTextBox.Text);}
            catch(ArgumentException) {SheetNamePreview.Text="Укажите название листа.";}
        }
        private void ParameterChanged(object sender,SelectionChangedEventArgs e) {RefreshPreview();}
        private void PreviewChanged(object sender,RoutedEventArgs e) {RefreshPreview();}
        private void SheetNameChanged(object sender,TextChangedEventArgs e) {RefreshPreview();}
        private void Ok_Click(object sender,RoutedEventArgs e)
        {

            ViewsGrid.CommitEdit(DataGridEditingUnit.Cell,true);ViewsGrid.CommitEdit(DataGridEditingUnit.Row,true);
            var parameter=ParameterComboBox.SelectedItem as Choice;
            if(UniqueCheckBox.IsChecked==true && string.IsNullOrEmpty(parameter?.Key)) {UiSupport.Error(this,ParameterComboBox,"Выберите параметр для отбора уникальных экземпляров.");return;}
            if(!(SectionTypeComboBox.SelectedItem is Choice section)) {UiSupport.Error(this,SectionTypeComboBox,"Выберите тип разреза.");return;}
            if(!int.TryParse(ScaleTextBox.Text,out int scale)||scale<1||scale>1000) {UiSupport.Error(this,ScaleTextBox,"Масштаб: целое число от 1 до 1000.");return;}
            if(!Algorithms.TryNumber(PaddingTextBox.Text,out double padding)||padding<=0||padding>10000) {UiSupport.Error(this,PaddingTextBox,"Отступ: больше 0 и не более 10000 мм.");return;}
            var options=new Options{ParameterKey=parameter?.Key??"",Unique=UniqueCheckBox.IsChecked==true,SectionTypeId=section.Id,Scale=scale,PaddingMm=padding,CreateSheet=CreateSheetCheckBox.IsChecked==true,SheetPrefix=SheetPrefixTextBox.Text,SheetSuffix=SheetSuffixTextBox.Text};
            foreach(var row in Rows.Where(r=>r.Enabled))
            {
                double offset=0;
                if(row.IsPlan && (!Algorithms.TryNumber(row.OffsetText,out offset)||offset<0)) {UiSupport.Error(this,ViewsGrid,"Введите неотрицательную отметку в миллиметрах: "+row.Label+".");return;}
                try {ElementViewMath.Name(row.Prefix,row.Token,row.Suffix);}
                catch(ArgumentException ex) {UiSupport.Error(this,ViewsGrid,ex.Message);return;}
                options.Views.Add(new ViewRequest{Kind=row.Kind,Label=row.Label,OffsetMm=offset,Prefix=row.Prefix,Suffix=row.Suffix,TemplateId=row.Template?.Id??-1});
            }
            if(options.Views.Count==0) {UiSupport.Error(this,ViewsGrid,"Выберите хотя бы один вид.");return;}
            if(options.CreateSheet)
            {
                if(!(TitleBlockComboBox.SelectedItem is Choice block)) {UiSupport.Error(this,TitleBlockComboBox,"Выберите основную надпись.");return;}
                options.TitleBlockId=block.Id;
            }
            Settings=options;DialogResult=true;
        }
        private void Cancel_Click(object sender,RoutedEventArgs e) {DialogResult=false;}
    }
}
