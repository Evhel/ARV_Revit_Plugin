using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
            ContextText.Text="Выбрано элементов: "+catalog.Elements.Count+".";
            var onlyTemplate=catalog.Templates.Where(c=>c.Id>0).ToList();
            foreach(var row in new[]{
                new ViewRow{Kind="Plan",Label="План 1",Enabled=true,OffsetText="1000",Suffix="_План1"},
                new ViewRow{Kind="Plan",Label="План 2",OffsetText="2000",Suffix="_План2"},
                new ViewRow{Kind="Plan",Label="План 3",OffsetText="3000",Suffix="_План3"},
                new ViewRow{Kind="Front",Label="Спереди",Enabled=true,OffsetText="—",Suffix="_Спереди"},
                new ViewRow{Kind="Back",Label="Сзади",OffsetText="—",Suffix="_Сзади"},
                new ViewRow{Kind="Left",Label="Слева",OffsetText="—",Suffix="_Слева"},
                new ViewRow{Kind="Right",Label="Справа",Enabled=true,OffsetText="—",Suffix="_Справа"}})
            {
                row.Number=Rows.Count+1;row.TitleSuffix="";row.Prefix="КЖ3_Арм_Пилон_";row.Templates=catalog.Templates;row.ViewportTypes=catalog.ViewportTypes;
                row.TemplateId=onlyTemplate.Count==1?onlyTemplate[0].Id:-1;row.ViewportTypeId=-1;Rows.Add(row);
            }
            InitializeSchedules();
            ViewsGrid.ItemsSource=Rows;ViewsGrid.SelectedIndex=0;
            ParameterComboBox.ItemsSource=catalog.Parameters;ParameterComboBox.SelectedIndex=0;
            SectionTypeComboBox.ItemsSource=catalog.SectionTypes;SectionTypeComboBox.SelectedIndex=0;
            TitleBlockComboBox.ItemsSource=catalog.TitleBlocks;TitleBlockComboBox.SelectedIndex=catalog.TitleBlocks.Count>0?0:-1;
            if(catalog.TitleBlocks.Count==0) {CreateSheetCheckBox.IsEnabled=false;ContextText.Text+=" Основные надписи не загружены: можно создать виды без листов.";}
            if(onlyTemplate.Count==0) ContextText.Text+=" В проекте нет шаблонов разрезов — создайте их перед запуском.";
            LayoutRowsCombo.ItemsSource=new[]{1,2,3,4};LayoutColumnsCombo.ItemsSource=new[]{1,2,3,4};
            LayoutRowsCombo.SelectedItem=2;LayoutColumnsCombo.SelectedItem=3;
            FillLayout_Click(null,null);
            RefreshPreview();
        }
        private void RefreshPreview()
        {
            if(catalog==null || ParameterComboBox==null || CountInfo==null || SheetNamePreview==null) return;
            var parameter=ParameterComboBox.SelectedItem as Choice;
            bool unique=UniqueCheckBox.IsChecked==true;
            var selected=catalog.Select(parameter?.Key,unique);
            int empty=unique?catalog.EmptyCount(parameter?.Key):0;
            CountInfo.Text="К обработке: "+selected.Count+" из "+catalog.Elements.Count+". Пустые исключены: "+empty+". Повторы: "+(catalog.Elements.Count-selected.Count-empty)+".";
            string token="Пм-1";
            foreach(var row in Rows) row.Token=token;
            foreach(var row in ScheduleRows)row.Token=token;
            try {SheetNamePreview.Text="Пример: "+ElementViewMath.Name(SheetPrefixTextBox.Text,token,SheetSuffixTextBox.Text);}
            catch(ArgumentException) {SheetNamePreview.Text="Укажите название листа.";}
        }
        private void ParameterChanged(object sender,SelectionChangedEventArgs e) {RefreshPreview();}
        private void PreviewChanged(object sender,RoutedEventArgs e) {RefreshPreview();}
        private void SheetNameChanged(object sender,TextChangedEventArgs e) {RefreshPreview();}
        private static double MarginValue(string text,string label)
        {
            if(!Algorithms.TryNumber(text,out double value)||value<0||value>10000) throw new ArgumentException("Отступ «"+label+"»: от 0 до 10000 мм.");
            return value;
        }
        public Options BuildOptions()
        {
            ViewsGrid.CommitEdit(DataGridEditingUnit.Cell,true);ViewsGrid.CommitEdit(DataGridEditingUnit.Row,true);
            var parameter=SelectedParameter();
            if(UniqueCheckBox.IsChecked==true && string.IsNullOrEmpty(parameter?.Key)) throw new ArgumentException("Выберите параметр для отбора уникальных экземпляров.");
            if(!(SectionTypeComboBox.SelectedItem is Choice section)) throw new ArgumentException("Выберите тип разреза.");
            if(catalog.Select(parameter?.Key,UniqueCheckBox.IsChecked==true).Count==0) throw new ArgumentException("После исключения пустых значений не осталось элементов.");
            var options=new Options{ParameterKey=parameter?.Key??"",Unique=UniqueCheckBox.IsChecked==true,SectionTypeId=section.Id,
                CreateSheet=CreateSheetCheckBox.IsChecked==true,UseLayout=UseLayoutCheckBox.IsChecked==true,LayoutRows=LayoutRowsCombo.SelectedItem is int lr?lr:2,LayoutColumns=LayoutColumnsCombo.SelectedItem is int lc?lc:3,LayoutCells=null,SheetPrefix=SheetPrefixTextBox.Text,SheetSuffix=SheetSuffixTextBox.Text};
            foreach(var row in Rows.Where(r=>r.Enabled))
            {
                double offset=0;
                if(row.IsPlan && (!Algorithms.TryNumber(row.OffsetText,out offset)||offset<0)) throw new ArgumentException("Введите неотрицательную отметку в мм: "+row.Label+".");
                if(row.TemplateId<=0 || !catalog.Templates.Any(c=>c.Id==row.TemplateId)) throw new ArgumentException("Выберите шаблон вида: "+row.Label+".");
                if(options.CreateSheet&&!catalog.ViewportTypes.Any(c=>c.Id==row.ViewportTypeId))throw new ArgumentException("Выберите тип видового экрана: "+row.Label+".");
                ElementViewMath.Name(row.Prefix,row.Token,row.Suffix);
                options.Views.Add(new ViewRequest{Number=row.Number,TitleSuffix=row.TitleSuffix,Kind=row.Kind,Label=row.Label,OffsetMm=offset,SideMm=MarginValue(row.SideText,row.Label+" — в стороны"),AboveMm=MarginValue(row.AboveText,row.Label+" — вверх"),BelowMm=MarginValue(row.BelowText,row.Label+" — вниз"),Prefix=row.Prefix,Suffix=row.Suffix,TemplateId=row.TemplateId,ViewportTypeId=row.ViewportTypeId});
            }
            options.LayoutCells=options.UseLayout&&options.Views.Count>0?ReadLayout():null;
            AddSchedules(options);
            if(options.Views.Count==0&&options.Schedules.Count==0) throw new ArgumentException("Выберите хотя бы один вид или спецификацию.");
            if(options.CreateSheet)
            {
                if(!(TitleBlockComboBox.SelectedItem is Choice block)) throw new ArgumentException("Выберите основную надпись.");
                options.SheetLeftMm=MarginValue(SheetLeftTextBox.Text,"лист слева");options.SheetTopMm=MarginValue(SheetTopTextBox.Text,"лист сверху");options.SheetGapMm=MarginValue(SheetGapTextBox.Text,"зазор видов");
                options.FormatA=FormatATextBox.Text.Trim();options.FormatX=FormatXTextBox.Text.Trim();
                options.TitleBlockId=block.Id;ElementViewMath.Name(options.SheetPrefix,Rows[0].Token,options.SheetSuffix);
                if(options.UseLayout&&options.Views.Count>0) ElementViewMath.ValidateLayout(options.LayoutCells,options.LayoutRows,options.LayoutColumns,options.Views.Select(v=>v.Number).ToArray());
            }
            return options;
        }
        public UserSettings CaptureSettings()
        {
            return new UserSettings{Schedules=CaptureScheduleRows(),ScheduleCells=ReadScheduleCells(),ScheduleGap=ScheduleGapTextBox.Text,Unique=UniqueCheckBox.IsChecked==true,CreateSheet=CreateSheetCheckBox.IsChecked==true,UseLayout=UseLayoutCheckBox.IsChecked==true,LayoutRows=LayoutRowsCombo.SelectedItem is int lr?lr:2,LayoutColumns=LayoutColumnsCombo.SelectedItem is int lc?lc:3,LayoutCells=ReadLayout(),
                SheetLeft=SheetLeftTextBox.Text,SheetTop=SheetTopTextBox.Text,SheetGap=SheetGapTextBox.Text,
                FormatA=FormatATextBox.Text,FormatX=FormatXTextBox.Text,SheetPrefix=SheetPrefixTextBox.Text,SheetSuffix=SheetSuffixTextBox.Text,
                Parameter=SavedChoice.From(SelectedParameter()),SectionType=SavedChoice.From(SectionTypeComboBox.SelectedItem as Choice),TitleBlock=SavedChoice.From(TitleBlockComboBox.SelectedItem as Choice),
                Rows=Rows.Select(row=>new SavedRow{Label=row.Label,Side=row.SideText,Above=row.AboveText,Below=row.BelowText,TitleSuffix=row.TitleSuffix,Enabled=row.Enabled,Prefix=row.Prefix,Suffix=row.Suffix,Offset=row.OffsetText,
                    Template=SavedChoice.From(catalog.Templates.FirstOrDefault(c=>c.Id==row.TemplateId)),Viewport=SavedChoice.From(catalog.ViewportTypes.FirstOrDefault(c=>c.Id==row.ViewportTypeId))}).ToList()};
        }
        public void ApplySettings(UserSettings saved)
        {
            if(saved==null||saved.Rows==null||saved.Rows.Count!=Rows.Count) throw new ArgumentException("Неполный файл настроек.");
            // Unavailable project references stay empty; independent settings still load.
            var parameter=saved.Parameter?.ResolveOptional(catalog.Parameters,"параметр",false);
            var section=saved.SectionType?.ResolveOptional(catalog.SectionTypes,"тип разреза",false);
            var block=saved.TitleBlock?.ResolveOptional(catalog.TitleBlocks,"основная надпись",false);
            var resolved=Rows.Select(row=>{
                var item=saved.Rows.SingleOrDefault(s=>s.Label==row.Label);
                if(item==null) throw new ArgumentException("В настройках нет строки «"+row.Label+"».");
                string oldPlan=saved.PlanPadding??"100";
                string side=saved.Version<4?(row.IsPlan?oldPlan:saved.Side??"100"):item.Side;
                string above=saved.Version<4?(row.IsPlan?oldPlan:saved.Above??"100"):item.Above;
                string below=saved.Version<4?(row.IsPlan?oldPlan:saved.Below??"100"):item.Below;
                MarginValue(side,row.Label+" — в стороны");MarginValue(above,row.Label+" — вверх");MarginValue(below,row.Label+" — вниз");
                return new {Target=row,Saved=item,Side=side,Above=above,Below=below,Template=item.Template?.ResolveOptional(catalog.Templates,"шаблон",false),Viewport=item.Viewport?.ResolveOptional(catalog.ViewportTypes,"тип видового экрана",false)};
            }).ToList();
            if(saved.UseLayout && saved.CreateSheet && resolved.Any(row=>row.Saved.Enabled))
                ElementViewMath.ValidateLayout(saved.LayoutCells,saved.LayoutRows,saved.LayoutColumns,resolved.Where(row=>row.Saved.Enabled).Select(row=>row.Target.Number).ToArray());
            string sheetLeft=saved.Version<3?"20":saved.SheetLeft,sheetTop=saved.Version<3?"20":saved.SheetTop,sheetGap=saved.Version<3?"10":saved.SheetGap;
            MarginValue(sheetLeft,"лист слева");MarginValue(sheetTop,"лист сверху");MarginValue(sheetGap,"зазор видов");
            var applySchedules=PrepareScheduleSettings(saved);
            applySchedules();
            ParameterComboBox.SelectedItem=parameter;if(parameter==null)ParameterComboBox.Text="";SectionTypeComboBox.SelectedItem=section;TitleBlockComboBox.SelectedItem=block;
            UniqueCheckBox.IsChecked=saved.Unique;CreateSheetCheckBox.IsChecked=saved.CreateSheet;UseLayoutCheckBox.IsChecked=saved.UseLayout;
            LayoutRowsCombo.SelectedItem=saved.LayoutRows>=1&&saved.LayoutRows<=4?saved.LayoutRows:2;LayoutColumnsCombo.SelectedItem=saved.LayoutColumns>=1&&saved.LayoutColumns<=4?saved.LayoutColumns:3;
            SetLayout(saved.LayoutCells);
            FormatATextBox.Text=saved.FormatA??"";FormatXTextBox.Text=saved.FormatX??"";
            SheetLeftTextBox.Text=sheetLeft;SheetTopTextBox.Text=sheetTop;SheetGapTextBox.Text=sheetGap;
            SheetPrefixTextBox.Text=saved.SheetPrefix;SheetSuffixTextBox.Text=saved.SheetSuffix;
            foreach(var row in resolved)
            {row.Target.SideText=row.Side;row.Target.AboveText=row.Above;row.Target.BelowText=row.Below;row.Target.TitleSuffix=row.Saved.TitleSuffix??"";row.Target.Enabled=row.Saved.Enabled;row.Target.Prefix=row.Saved.Prefix;row.Target.Suffix=row.Saved.Suffix;row.Target.OffsetText=row.Saved.Offset;row.Target.TemplateId=row.Template?.Id??-1;row.Target.ViewportTypeId=row.Viewport?.Id??(row.Saved.Viewport!=null?-2:-1);}
            RefreshPreview();ValidationMessage.Visibility=Visibility.Collapsed;
        }
        private readonly List<TextBox> layoutBoxes=new List<TextBox>();
        private Choice SelectedParameter()
        {
            var selected=ParameterComboBox.SelectedItem as Choice;
            string typed=(ParameterComboBox.Text??"").Trim();
            if(selected!=null && selected.Name==typed)return selected;
            var matches=catalog.Parameters.Where(p=>p.Name.IndexOf(typed,StringComparison.CurrentCultureIgnoreCase)>=0 && !string.IsNullOrEmpty(p.Key)).ToList();
            if(typed.Length>0 && matches.Count==1)return matches[0];
            if(UniqueCheckBox.IsChecked==true)throw new ArgumentException("Выберите однозначный параметр из списка по введённому названию.");
            return selected;
        }
        private int[] ReadLayout()
        {
            return layoutBoxes.Select(box=>{
                if(string.IsNullOrWhiteSpace(box.Text))return 0;
                if(!int.TryParse(box.Text,out int n)||n<1||n>Rows.Count)throw new ArgumentException("В ячейке схемы укажите № вида от 1 до 7 либо оставьте её пустой.");
                return n;
            }).ToArray();
        }
        private void SetLayout(int[] cells)
        {
            for(int i=0;i<layoutBoxes.Count;i++)layoutBoxes[i].Text=cells!=null&&i<cells.Length&&cells[i]>0?cells[i].ToString():"";
        }
        private void LayoutSizeChanged(object sender,SelectionChangedEventArgs e)
        {
            if(LayoutGrid==null||layoutBoxes==null||!(LayoutRowsCombo.SelectedItem is int rows)||!(LayoutColumnsCombo.SelectedItem is int columns))return;
            var old=layoutBoxes.Select(b=>b.Text).ToList();
            LayoutGrid.Children.Clear();LayoutGrid.RowDefinitions.Clear();LayoutGrid.ColumnDefinitions.Clear();layoutBoxes.Clear();
            for(int i=0;i<rows;i++)LayoutGrid.RowDefinitions.Add(new RowDefinition());
            for(int i=0;i<columns;i++)LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<rows*columns;i++)
            {
                var box=new TextBox{Text=i<old.Count?old[i]:"",FontSize=20,TextAlignment=TextAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(3),ToolTip="№ вида из таблицы; пусто — свободная ячейка"};
                Grid.SetRow(box,i/columns);Grid.SetColumn(box,i%columns);LayoutGrid.Children.Add(box);layoutBoxes.Add(box);
            }
        }
        private void FillLayout_Click(object sender,RoutedEventArgs e)
        {
            var enabled=Rows.Where(row=>row.Enabled).Select(row=>row.Number).ToArray();
            if(enabled.Length>layoutBoxes.Count) {UiSupport.Error(this,LayoutRowsCombo,"Добавьте ячейки: включённых видов больше, чем мест в схеме.");return;}
            SetLayout(enabled);
        }
        private void Save_Click(object sender,RoutedEventArgs e)
        {
            var dialog=new SaveFileDialog{Filter="Настройки ARV (*.json)|*.json",DefaultExt=".json",FileName="ARV_Виды_элемента.json",AddExtension=true,OverwritePrompt=true};
            if(dialog.ShowDialog(this)!=true)return;
            try {SettingsFile.Save(dialog.FileName,CaptureSettings());FileStatusText.Text="Сохранено: "+Path.GetFileName(dialog.FileName);ValidationMessage.Visibility=Visibility.Collapsed;}
            catch(Exception ex) {UiSupport.Error(this,null,"Не удалось сохранить настройки: "+ex.Message);}
        }
        private void Load_Click(object sender,RoutedEventArgs e)
        {
            var dialog=new OpenFileDialog{Filter="Настройки ARV (*.json)|*.json",CheckFileExists=true};
            if(dialog.ShowDialog(this)!=true)return;
            try {ApplySettings(SettingsFile.Load(dialog.FileName));FileStatusText.Text="Загружено: "+Path.GetFileName(dialog.FileName);}
            catch(Exception ex) {UiSupport.Error(this,null,"Не удалось загрузить настройки: "+ex.Message);}
        }
        private void Ok_Click(object sender,RoutedEventArgs e)
        {
            try {Settings=BuildOptions();DialogResult=true;}
            catch(ArgumentException ex) {UiSupport.Error(this,ViewsGrid,ex.Message);}
        }
        private void Cancel_Click(object sender,RoutedEventArgs e) {DialogResult=false;}
    }
}
