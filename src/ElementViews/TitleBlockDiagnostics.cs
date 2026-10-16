using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace ARV.ElementViews
{
    internal static class TitleBlockDiagnostics
    {
        internal static string Read(Document doc,int symbolId)
        {
            var symbol=doc.GetElement(new ElementId(symbolId)) as FamilySymbol;
            if(symbol==null)throw new InvalidOperationException("Выберите существующий тип основной надписи.");
            var text=new StringBuilder();
            text.AppendLine("ARV — параметры основной надписи");
            text.AppendLine("Проект: "+doc.Title);text.AppendLine("Семейство: "+symbol.FamilyName);text.AppendLine("Тип: "+symbol.Name+"; ID: "+symbol.Id.IntegerValue);
            text.AppendLine("Только чтение. Транзакции и сохранение семейства не выполняются.");
            ProjectParameters(text,symbol,"ПАРАМЕТРЫ ТИПА В ПРОЕКТЕ");
            var instances=new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>().Where(e=>e.Symbol.Id==symbol.Id).OrderBy(e=>e.Id.IntegerValue).ToList();
            text.AppendLine("Экземпляров выбранного типа в проекте: "+instances.Count);
            if(instances.Count>0)ProjectParameters(text,instances[0],"ПАРАМЕТРЫ ЭКЗЕМПЛЯРА "+instances[0].Id.IntegerValue+" (первый существующий)");
            Document familyDoc=null;
            try
            {
                if(!symbol.Family.IsEditable)throw new InvalidOperationException("Семейство недоступно для EditFamily.");
                familyDoc=doc.EditFamily(symbol.Family);
                var manager=familyDoc.FamilyManager;
                var familyType=manager.Types.Cast<FamilyType>().FirstOrDefault(t=>t.Name==symbol.Name)??manager.CurrentType;
                text.AppendLine();text.AppendLine("ОПРЕДЕЛЕНИЯ ПАРАМЕТРОВ В ФАЙЛЕ СЕМЕЙСТВА");
                text.AppendLine("Тип для чтения значений: "+(familyType?.Name??"нет"));
                foreach(FamilyParameter parameter in manager.Parameters.Cast<FamilyParameter>().OrderBy(p=>p.Definition.Name,StringComparer.Ordinal))
                {
                    text.AppendLine(Name(parameter.Definition.Name)+" | "+(parameter.IsInstance?"Экземпляр":"Тип")+" | "+parameter.StorageType+" | ReadOnly="+parameter.IsReadOnly+" | ID="+parameter.Id.IntegerValue);
                    text.AppendLine("  Формула: "+(parameter.Formula??"нет"));
                    if(familyType!=null)
                    {
                        try
                        {
                            string value=parameter.StorageType==StorageType.String?familyType.AsString(parameter):familyType.AsValueString(parameter);
                            text.AppendLine("  Значение: "+(value??"<пусто>"));
                        }
                        catch(Exception ex){text.AppendLine("  Значение недоступно: "+ex.Message);}
                    }
                }
            }
            catch(Exception ex){text.AppendLine("Чтение файла семейства недоступно: "+ex.Message+" Параметры проекта приведены выше.");}
            finally
            {
                if(familyDoc!=null)
                try {if(!familyDoc.Close(false))text.AppendLine("Не удалось закрыть документ семейства без сохранения.");}
                catch(Exception ex){text.AppendLine("Закрытие семейства без сохранения не выполнено: "+ex.Message);}
            }
            return text.ToString();
        }
        private static void ProjectParameters(StringBuilder text,Element element,string heading)
        {
            text.AppendLine();text.AppendLine(heading);
            foreach(Parameter parameter in element.Parameters.Cast<Parameter>().Where(p=>p.Definition!=null).OrderBy(p=>p.Definition.Name,StringComparer.Ordinal))
            {
                text.AppendLine(Name(parameter.Definition.Name)+" | "+parameter.StorageType+" | ReadOnly="+parameter.IsReadOnly+" | ID="+parameter.Id.IntegerValue);
                try {text.AppendLine("  Значение: "+(parameter.StorageType==StorageType.String?parameter.AsString():parameter.AsValueString()));}
                catch(Exception ex){text.AppendLine("  Значение недоступно: "+ex.Message);}
            }
        }
        private static string Name(string name)
        {
            return "«"+name+"» ["+string.Join(" ",name.Select(ch=>"U+"+((int)ch).ToString("X4",CultureInfo.InvariantCulture)))+"]";
        }
    }
}
