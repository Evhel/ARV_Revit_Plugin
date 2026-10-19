using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARV.Common;
namespace ARV.ElementViews
{
    public partial class MainWindow
    {
        public List<ScheduleRow> ScheduleRows {get;}=new List<ScheduleRow>();
        private readonly List<TextBox> scheduleBoxes=new List<TextBox>();
        private void InitializeSchedules()
        {
            for(int i=1;i<=10;i++)ScheduleRows.Add(new ScheduleRow{Number=i,AllSchedules=catalog.Schedules});
            SchedulesGrid.ItemsSource=ScheduleRows;
            for(int r=0;r<5;r++)ScheduleLayoutGrid.RowDefinitions.Add(new RowDefinition());
            for(int c=0;c<2;c++)ScheduleLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<10;i++)
            {
                var box=new TextBox{FontSize=14,TextAlignment=TextAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(1),MinHeight=0,Padding=new Thickness(1),ToolTip="№ спецификации из таблицы 1–10"};
                Grid.SetRow(box,i/2);Grid.SetColumn(box,i%2);ScheduleLayoutGrid.Children.Add(box);scheduleBoxes.Add(box);
            }
        }
        private void ScheduleSearchChanged(object sender,TextChangedEventArgs e)
        {
            var combo=sender as ComboBox;var row=combo?.DataContext as ScheduleRow;
            if(row==null||row.Searching)return;
            var editor=e.OriginalSource as TextBox;
            string query=editor?.Text??combo.Text;int caret=editor?.CaretIndex??query.Length;
            row.Searching=true;
            try
            {
                row.SearchText=query;
                combo.ItemsSource=row.FindMatches(query);
                combo.Text=query;
                if(row.Source?.Name==query)combo.SelectedItem=row.Source;
                if(editor!=null)editor.CaretIndex=Math.Min(caret,editor.Text.Length);
                if(combo.IsKeyboardFocusWithin && query!=row.Source?.Name)combo.IsDropDownOpen=true;
            }
            finally{row.Searching=false;}
            if(query.Length==0)row.Source=null;
        }
        private int[] ReadScheduleCells()
        {
            return scheduleBoxes.Select(box=>{
                if(string.IsNullOrWhiteSpace(box.Text))return 0;
                if(!int.TryParse(box.Text,out int n)||n<1||n>10)throw new ArgumentException("В схеме спецификаций укажите № 1–10 либо оставьте ячейку пустой.");
                return n;
            }).ToArray();
        }
        private void SetScheduleCells(int[] cells)
        {
            for(int i=0;i<scheduleBoxes.Count;i++)scheduleBoxes[i].Text=cells!=null&&i<cells.Length&&cells[i]>0?cells[i].ToString():"";
        }
        private void FillScheduleLayout_Click(object sender,RoutedEventArgs e)
        {
            var numbers=ScheduleRows.Where(row=>row.Source!=null).Select(row=>row.Number).ToList();
            var cells=new int[10];
            for(int i=0;i<numbers.Count;i++)cells[(i%5)*2+(i<5?1:0)]=numbers[i];
            SetScheduleCells(cells);
        }
        private void AddSchedules(Options options)
        {
            SchedulesGrid.CommitEdit(DataGridEditingUnit.Cell,true);SchedulesGrid.CommitEdit(DataGridEditingUnit.Row,true);
            foreach(var row in ScheduleRows)
            {
                if(row.Source==null&&string.IsNullOrWhiteSpace(row.SearchText))continue;
                if(row.Source?.Name!=row.SearchText)
                {
                    var exact=row.AllSchedules.FirstOrDefault(s=>s.Name==row.SearchText);
                    var matches=exact==null?row.FindMatches(row.SearchText):new List<ScheduleInfo>{exact};
                    if(matches.Count!=1)throw new ArgumentException("Выберите однозначную спецификацию в строке "+row.Number+".");
                    row.Source=matches[0];
                }
                if(!options.CreateSheet)throw new ArgumentException("Для спецификаций включите создание листа на вкладке «Листы».");
                if(row.Copy&&!row.CanCopy)throw new ArgumentException("Спецификацию нельзя копировать: "+row.Source.Name);
                if(row.Copy&&row.Filters.Count>0&&(row.Filter==null||!row.Filter.Operators.Any(o=>o.Key==row.OperatorKey)))throw new ArgumentException("Выберите фильтр и допустимое условие: строка "+row.Number+".");
                options.Schedules.Add(new ScheduleRequest{Number=row.Number,SourceId=row.Source.Id,SourceName=row.Source.Name,Copy=row.Copy,
                    ValueKind=row.Copy?row.Filter?.ValueKind:null,FilterIndex=row.Copy?row.Filter?.Index??-1:-1,FilterName=row.Copy?row.Filter?.FieldName:null,OperatorKey=row.Copy?row.OperatorKey:null,
                    ViewParameterId=row.Copy?row.ViewParameter?.Id??-1:-1,ViewParameterName=row.Copy?row.ViewParameter?.Name:null});
            }
            if(options.Schedules.Where(s=>!s.Copy).GroupBy(s=>s.SourceId).Any(g=>g.Count()>1))throw new ArgumentException("Одну исходную спецификацию нельзя разместить дважды на одном листе без копирования.");
            options.ScheduleGapMm=MarginValue(ScheduleGapTextBox.Text,"между столбцами спецификаций");
            options.ScheduleCells=ReadScheduleCells();
            if(options.Schedules.Count>0)
            {
                if(options.ScheduleCells.All(n=>n==0))FillScheduleLayout_Click(null,null);
                options.ScheduleCells=ReadScheduleCells();
                ScheduleLayoutMath.Validate(options.ScheduleCells,options.Schedules.Select(s=>s.Number).ToArray());
            }
        }
        private List<SavedScheduleRow> CaptureScheduleRows()
        {
            if(ScheduleRows.Any(row=>row.Source!=null)&&ReadScheduleCells().All(n=>n==0))FillScheduleLayout_Click(null,null);
            return ScheduleRows.Select(row=>new SavedScheduleRow{Number=row.Number,Source=row.Source==null?null:new SavedChoice{Id=row.Source.Id,Name=row.Source.Name},Copy=row.Copy,
                FilterIndex=row.Filter?.Index??-1,FilterName=row.Filter?.FieldName,OperatorKey=row.OperatorKey,ViewParameter=SavedChoice.From(row.ViewParameter)}).ToList();
        }
        private Action PrepareScheduleSettings(UserSettings saved)
        {
            if(saved.Version<5)return ()=>{SchedulesGrid.ItemsSource=null;foreach(var row in ScheduleRows){row.Source=null;row.Copy=true;}SetScheduleCells(null);ScheduleGapTextBox.Text="10";SchedulesGrid.ItemsSource=ScheduleRows;};
            if(saved.Schedules==null||saved.Schedules.Count!=10||saved.Schedules.Any(s=>s==null)||saved.Schedules.Select(s=>s.Number).Distinct().Count()!=10)throw new ArgumentException("Неполные настройки спецификаций.");
            MarginValue(saved.ScheduleGap,"между столбцами спецификаций");
            var prepared=ScheduleRows.Select(row=>{
                var item=saved.Schedules.SingleOrDefault(s=>s.Number==row.Number);
                if(item==null)throw new ArgumentException("Не найдена строка спецификации "+row.Number);
                var source=item.Source==null?null:catalog.Schedules.FirstOrDefault(s=>s.Id==item.Source.Id&&s.Name==item.Source.Name);
                if(source==null&&item.Source!=null)
                {
                    var named=catalog.Schedules.Where(s=>s.Name==item.Source.Name).ToList();
                    if(named.Count!=1)throw new ArgumentException("В проекте не найдена спецификация «"+item.Source.Name+"».");source=named[0];
                }
                ScheduleFilterChoice filter=null;Choice parameter=null;
                if(source!=null)
                {
                    if(item.Copy&&!source.CanCopy)throw new ArgumentException("Копирование недоступно: "+source.Name);
                    filter=source.Filters.FirstOrDefault(f=>f.Index==item.FilterIndex&&f.FieldName==item.FilterName);
                    if(filter==null&&item.FilterName!=null)
                    {var matches=source.Filters.Where(f=>f.FieldName==item.FilterName).ToList();if(matches.Count==1)filter=matches[0];}
                    if(item.Copy&&source.Filters.Count>0&&(filter==null||!filter.Operators.Any(o=>o.Key==item.OperatorKey)))throw new ArgumentException("Не найден сохранённый фильтр/условие: "+source.Name);
                    parameter=item.ViewParameter?.ResolveOptional(source.Parameters,"параметр спецификации",item.Copy&&item.ViewParameter?.Id!=-1)??source.Parameters.FirstOrDefault();
                }
                return new {Row=row,Saved=item,Source=source,Filter=filter,Parameter=parameter};
            }).ToList();
            ScheduleLayoutMath.Validate(saved.ScheduleCells??new int[10],prepared.Where(p=>p.Source!=null).Select(p=>p.Row.Number).ToArray());
            return ()=>{
                SchedulesGrid.ItemsSource=null;
                foreach(var p in prepared){p.Row.Source=p.Source;p.Row.Copy=p.Saved.Copy;p.Row.Filter=p.Filter;p.Row.OperatorKey=p.Saved.OperatorKey;p.Row.ViewParameter=p.Parameter;}
                SetScheduleCells(saved.ScheduleCells);ScheduleGapTextBox.Text=saved.ScheduleGap;SchedulesGrid.ItemsSource=ScheduleRows;
            };
        }
    }
}
