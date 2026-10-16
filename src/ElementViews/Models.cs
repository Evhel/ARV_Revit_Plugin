using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace ARV.ElementViews
{
    public sealed class Choice
    {
        public int Id {get;set;}
        public string Key {get;set;}
        public string Name {get;set;}
        public override string ToString() => Name;
    }
    public sealed class ElementData
    {
        public int Id;
        public string Name, OrientationNote;
        public Point3[] Corners;
        public Point3 AxisX, AxisY;
        public readonly Dictionary<string,string> Values=new Dictionary<string,string>();
        public readonly Dictionary<string,string> GroupKeys=new Dictionary<string,string>();
        public ElementCandidate Candidate(string key)
        {
            string value,groupKey;Values.TryGetValue(key??"",out value);GroupKeys.TryGetValue(key??"",out groupKey);
            return new ElementCandidate {Id=Id,GroupKey=groupKey,Z=Corners.Min(p=>p.Z),X=Corners.Min(p=>p.X),Y=Corners.Max(p=>p.Y),Value=value};
        }
    }
    public sealed class Catalog
    {
        public List<ElementData> Elements {get;set;}=new List<ElementData>();
        public List<Choice> Parameters {get;set;}=new List<Choice>();
        public List<Choice> SectionTypes {get;set;}=new List<Choice>();
        public List<Choice> Templates {get;set;}=new List<Choice>();
        public List<Choice> TitleBlocks {get;set;}=new List<Choice>();
        public List<Choice> ViewportTypes {get;set;}=new List<Choice>();
        public int UnsupportedCount {get;set;}
        public int EmptyCount(string key)=>Elements.Count(e=>string.IsNullOrWhiteSpace(e.Candidate(key).Value));
        public List<ElementCandidate> Select(string key,bool unique) => ElementViewMath.Select(Elements.Select(e=>e.Candidate(key)),unique);
    }
    public sealed class ViewRow : INotifyPropertyChanged
    {
        private string prefix,suffix,token,offset,titleSuffix,side="100",above="100",below="100";
        private int templateId=-1,viewportTypeId=-1;
        private bool enabled;
        public int Number {get;set;}
        public string Kind {get;set;}
        public string Label {get;set;}
        public bool Enabled {get=>enabled;set{enabled=value;Changed();}}
        public bool IsPlan => Kind=="Plan";
        public string Prefix {get=>prefix;set{prefix=value;Changed();}}
        public string Suffix {get=>suffix;set{suffix=value;Changed();}}
        public string TitleSuffix {get=>titleSuffix;set{titleSuffix=value;Changed();}}
        public string Token {get=>token;set{token=value;Changed();}}
        public string SideText {get=>side;set{side=value;Changed();}}
        public string AboveText {get=>above;set{above=value;Changed();}}
        public string BelowText {get=>below;set{below=value;Changed();}}
        public string OffsetText {get=>offset;set{offset=value;Changed();}}
        public List<Choice> Templates {get;set;}
        public List<Choice> ViewportTypes {get;set;}
        public int TemplateId {get=>templateId;set{templateId=value;Changed();}}
        public int ViewportTypeId {get=>viewportTypeId;set{viewportTypeId=value;Changed();}}
        public string Preview { get { try{return ElementViewMath.Name(Prefix,Token,Suffix);}catch(ArgumentException){return "—";} } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed() {PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));}
    }
    public sealed class ViewRequest
    {
        public string Kind,Label,Prefix,Suffix,TitleSuffix;
        public int Number;
        public double OffsetMm,SideMm,AboveMm,BelowMm;
        public int TemplateId,ViewportTypeId;
    }
    public sealed class Options
    {
        public string ParameterKey,SheetPrefix,SheetSuffix,FormatA,FormatX;
        public bool Unique,CreateSheet,UseLayout;
        public int LayoutRows=2,LayoutColumns=3;
        public int[] LayoutCells;
        public int SectionTypeId,TitleBlockId;
        public double SheetLeftMm=20,SheetTopMm=20,SheetGapMm=10;
        public List<ViewRequest> Views=new List<ViewRequest>();
    }
    public sealed class SkippedElement
    {
        public string Element {get;set;}
        public string Reason {get;set;}
    }
    public sealed class CreationResult
    {
        public int Selected,Filtered,EmptyExcluded,Completed,Views,Sheets;
        public List<SkippedElement> Skipped=new List<SkippedElement>();
        public List<SkippedElement> Warnings=new List<SkippedElement>();
    }
}
