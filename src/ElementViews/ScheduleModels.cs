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
                if(source==value){SearchText=value?.Name??"";Changed();return;}
                source=value;SearchText=value?.Name??"";
                if(value!=null&&!value.CanCopy)copy=false;
                filter=null;operatorKey=null;
                ViewParameter=value?.Parameters.FirstOrDefault();Changed();
            }
        }
        public bool Copy {get=>copy;set{copy=value;Changed();}}
        public bool CanCopy=>Source?.CanCopy==true;
        public bool CanEdit=>Copy&&Source!=null;
        public IList<ScheduleFilterChoice> Filters=>Source?.Filters??new List<ScheduleFilterChoice>();
        public IList<Choice> Parameters=>Source?.Parameters??new List<Choice>();
        public ScheduleFilterChoice Filter {get=>filter;set{filter=value;operatorKey=value?.OperatorKey;Changed();}}
        public IList<Choice> Operators=>Filter?.Operators??new List<Choice>();
        public string OperatorKey {get=>operatorKey;set{operatorKey=value;Changed();}}
        public Choice ViewParameter {get=>viewParameter;set{viewParameter=value;Changed();}}
        public string Token {get=>token;set{token=value;Changed();}}
        public string FilterValue=>Filter==null?"—":OperatorKey=="HasParameter"||OperatorKey=="HasValue"||OperatorKey=="HasNoValue"?"Не требуется":Token;
        public List<ScheduleInfo> FindMatches(string text)=>(AllSchedules??new List<ScheduleInfo>()).Where(s=>s.Name.IndexOf((text??"").Trim(),StringComparison.CurrentCultureIgnoreCase)>=0).ToList();
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(){PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));}
    }
    public sealed class ScheduleRequest
    {
        public int Number,SourceId,FilterIndex=-1,ViewParameterId=-1;
        public string SourceName,FilterName,OperatorKey,ViewParameterName,ValueKind;
        public bool Copy;
    }
}
