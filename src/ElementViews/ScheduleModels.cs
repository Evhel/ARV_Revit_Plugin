using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
namespace ARV.ElementViews
{
    public sealed class ScheduleFilterChoice
    {
        public int Index {get;set;}
        public string Name {get;set;}
        public string FieldName {get;set;}
        public string ValueKind {get;set;}
        public string OperatorKey {get;set;}
        public List<Choice> Operators {get;set;}=new List<Choice>();
        public override string ToString()=>Name;
    }
    public sealed class ScheduleInfo
    {
        public int Id {get;set;}
        public string Name {get;set;}
        public bool CanCopy {get;set;}
        public List<ScheduleFilterChoice> Filters {get;set;}=new List<ScheduleFilterChoice>();
        public List<Choice> Parameters {get;set;}=new List<Choice>();
        public override string ToString()=>Name;
    }
    public sealed class ScheduleRow : INotifyPropertyChanged
    {
        private ScheduleInfo source;
        private bool copy=true;
        private ScheduleFilterChoice filter;
        private Choice viewParameter;
        private string operatorKey,token="Пм-1";
        public ScheduleFilterChoice KeepFilters {get;}=new ScheduleFilterChoice{Index=-1,Name="Не менять фильтр"};
        public int Number {get;set;}
        public List<ScheduleInfo> AllSchedules {get;set;}
        public bool Searching;
        public string SearchText="";
        public ScheduleInfo Source
        {
            get=>source;
            set
            {
                if(Searching)return;
                if(source==value){SearchText=value?.Name??"";return;}
                source=value;SearchText=value?.Name??"";
                if(value!=null&&!value.CanCopy)copy=false;
                filter=value==null?null:KeepFilters;operatorKey=null;
                viewParameter=value?.Parameters.FirstOrDefault();
                Changed(nameof(Source),nameof(Copy),nameof(CanCopy),nameof(CanEdit),nameof(Filters),nameof(Parameters),nameof(Filter),nameof(Operators),nameof(OperatorKey),nameof(ViewParameter),nameof(FilterValue),nameof(CanEditFilter));
            }
        }
        public bool Copy {get=>copy;set{if(copy==value)return;copy=value;Changed(nameof(Copy),nameof(CanEdit),nameof(CanEditFilter));}}
        public bool CanCopy=>Source?.CanCopy==true;
        public bool CanEdit=>Copy&&Source!=null;
        public bool CanEditFilter=>CanEdit&&Filter!=null&&Filter.Index>=0;
        public IList<ScheduleFilterChoice> Filters=>Source==null?new List<ScheduleFilterChoice>():new[]{KeepFilters}.Concat(Source.Filters).ToList();
        public IList<Choice> Parameters=>Source?.Parameters??new List<Choice>();
        public ScheduleFilterChoice Filter {get=>filter;set{if(filter==value)return;RestoreFilter(value,value?.OperatorKey);}}
        internal void RestoreFilter(ScheduleFilterChoice value,string condition)
        {
            filter=value;operatorKey=condition;
            Changed(nameof(Filter),nameof(Operators),nameof(OperatorKey),nameof(FilterValue),nameof(CanEditFilter));
        }
        public IList<Choice> Operators=>Filter?.Operators??new List<Choice>();
        public string OperatorKey
        {
            get=>operatorKey;
            set
            {
                // ItemsSource replacement temporarily clears SelectedValue; keep the filter's native condition.
                if(value==null&&Filter!=null)return;
                if(operatorKey==value)return;
                operatorKey=value;Changed(nameof(OperatorKey),nameof(FilterValue));
            }
        }
        public Choice ViewParameter {get=>viewParameter;set{if(viewParameter==value)return;viewParameter=value;Changed(nameof(ViewParameter));}}
        public string Token {get=>token;set{if(token==value)return;token=value;Changed(nameof(Token),nameof(FilterValue));}}
        public string FilterValue=>Filter==null||Filter.Index<0?"—":OperatorKey=="HasParameter"||OperatorKey=="HasValue"||OperatorKey=="HasNoValue"?"Не требуется":Token;
        public List<ScheduleInfo> FindMatches(string text)=>(AllSchedules??new List<ScheduleInfo>()).Where(s=>s.Name.IndexOf((text??"").Trim(),StringComparison.CurrentCultureIgnoreCase)>=0).ToList();
        public event PropertyChangedEventHandler PropertyChanged;
        // Notify only dependent bindings: refreshing Source during a filter selection can reset the selectors.
        private void Changed(params string[] names){foreach(var name in names)PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));}
    }
    public sealed class ScheduleRequest
    {
        public int Number,SourceId,FilterIndex=-1,ViewParameterId=-1;
        public string SourceName,FilterName,OperatorKey,ViewParameterName,ValueKind;
        public bool Copy;
    }
}
