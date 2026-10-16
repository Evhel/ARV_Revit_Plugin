using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ARV.ElementViews
{
    [DataContract]
    public sealed class SavedChoice
    {
        [DataMember] public int Id;
        [DataMember] public string Name;
        [DataMember] public string Key;
        public static SavedChoice From(Choice c)=>c==null?null:new SavedChoice{Id=c.Id,Name=c.Name,Key=c.Key};
        public Choice ResolveOptional(IList<Choice> choices,string label,bool required)
        {
            try{return Resolve(choices,label);}
            catch(InvalidOperationException) {if(required)throw;return null;}
        }
        public Choice Resolve(IList<Choice> choices,string label)
        {
            var exact=choices.FirstOrDefault(c=>c.Id==Id && c.Name==Name && c.Key==Key);
            if(exact!=null)return exact;
            var named=choices.Where(c=>c.Name==Name).ToList();
            if(named.Count!=1) throw new InvalidOperationException("В проекте не найден однозначный выбор «"+Name+"» ("+label+"). Выберите его вручную и сохраните новый файл.");
            return named[0];
        }
    }
    [DataContract]
    public sealed class SavedRow
    {
        [DataMember] public string Label,Prefix,Suffix,Offset,TitleSuffix;
        [DataMember] public bool Enabled;
        [DataMember] public SavedChoice Template,Viewport;
    }
    [DataContract]
    public sealed class UserSettings
    {
        [DataMember] public int Version=2;
        [DataMember] public bool Unique,CreateSheet,UseLayout;
        [DataMember] public int LayoutRows=2,LayoutColumns=3;
        [DataMember] public int[] LayoutCells;
        [DataMember] public string Side="100",Above="100",Below="100",PlanPadding="100",SheetPrefix,SheetSuffix;
        [DataMember] public SavedChoice Parameter,SectionType,TitleBlock;
        [DataMember] public List<SavedRow> Rows=new List<SavedRow>();
    }
    public static class SettingsFile
    {
        public static void Save(string path,UserSettings settings)
        {
            if(settings==null) throw new ArgumentNullException(nameof(settings));
            using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write))
                new DataContractJsonSerializer(typeof(UserSettings)).WriteObject(stream,settings);
        }
        public static UserSettings Load(string path)
        {
            var info=new FileInfo(path);
            if(info.Length>1024*1024) throw new InvalidDataException("Файл настроек слишком большой.");
            UserSettings settings;
            using(var stream=info.OpenRead()) settings=(UserSettings)new DataContractJsonSerializer(typeof(UserSettings)).ReadObject(stream);
            if(settings==null || (settings.Version!=1 && settings.Version!=2) || settings.Rows==null || settings.Rows.Count!=7 || settings.Rows.Any(row=>row==null) || settings.Rows.Select(row=>row.Label).Distinct().Count()!=7)
                throw new InvalidDataException("Неподдерживаемый или неполный файл настроек.");
            return settings;
        }
    }
}
