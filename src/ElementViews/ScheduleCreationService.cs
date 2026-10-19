using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.Common;
namespace ARV.ElementViews
{
    internal sealed class PreparedSchedule
    {
        internal ScheduleRequest Request;
        internal ViewSchedule View;
        internal bool Copied;
    }
    internal sealed class SchedulePlacement
    {
        internal int Count;
        internal double? Left;
    }
    internal static class ScheduleCreationService
    {
        internal static List<PreparedSchedule> Create(Document doc,Options options,string token,string raw,HashSet<string> names,List<string> warnings)
        {
            var result=new List<PreparedSchedule>();
            foreach(var request in options.Schedules)
            {
                using(var stage=new SubTransaction(doc))
                {
                    stage.Start();
                    try
                    {
                        var source=doc.GetElement(new ElementId(request.SourceId)) as ViewSchedule;
                        if(source==null||source.IsTemplate||source.IsTitleblockRevisionSchedule||source.IsInternalKeynoteSchedule)throw new InvalidOperationException("Исходная спецификация недоступна.");
                        if(!request.Copy)
                        {
                            stage.RollBack();result.Add(new PreparedSchedule{Request=request,View=source});continue;
                        }
                        if(!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))throw new InvalidOperationException("Revit не разрешает дублирование.");
                        var originalFilters=Enumerable.Range(0,source.Definition.GetFilterCount()).Select(i=>ScheduleFilterTools.Signature(source.Definition.GetFilter(i))).ToArray();
                        var copy=doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate)) as ViewSchedule;
                        if(copy==null||copy.Id==source.Id)throw new InvalidOperationException("Не получена независимая копия спецификации.");
                        var trialNames=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);
                        copy.Name=Algorithms.UniqueName(ElementViewMath.Name(source.Name,"_",token),trialNames);
                        ScheduleFilter replacement=null;
                        if(request.FilterIndex>=0)
                        {
                            if(request.FilterIndex>=copy.Definition.GetFilterCount())throw new InvalidOperationException("Изменился список фильтров исходной спецификации.");
                            var old=copy.Definition.GetFilter(request.FilterIndex);var field=copy.Definition.GetField(old.FieldId);
                            if(field.GetName()!=request.FilterName)throw new InvalidOperationException("Изменился выбранный параметр фильтра.");
                            var type=(ScheduleFilterType)Enum.Parse(typeof(ScheduleFilterType),request.OperatorKey);
                            replacement=ScheduleFilterTools.BuildFilter(doc,old.FieldId,field.GetSpecTypeId(),request.ValueKind,type,token,raw);
                            copy.Definition.SetFilter(request.FilterIndex,replacement);
                        }
                        Parameter parameter=null;object expected=null;
                        if(request.ViewParameterId!=-1)
                        {
                            parameter=copy.Parameters.Cast<Parameter>().FirstOrDefault(p=>p.Id.IntegerValue==request.ViewParameterId&&p.Definition.Name==request.ViewParameterName);
                            if(parameter==null||parameter.IsReadOnly||parameter.Id.IntegerValue==(int)BuiltInParameter.VIEW_NAME)throw new InvalidOperationException("Параметр экземпляра вида недоступен для записи.");
                            expected=WriteParameter(doc,parameter,token,raw);
                        }
                        doc.Regenerate();
                        ScheduleFilterTools.Verify(copy.Definition,originalFilters,request.FilterIndex,replacement);
                        ScheduleFilterTools.Verify(source.Definition,originalFilters,-1,null);
                        if(parameter!=null&&!ParameterMatches(parameter,expected))throw new InvalidOperationException("Revit не сохранил марку в параметре вида «"+request.ViewParameterName+"».");
                        if(stage.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Не подтверждено создание спецификации.");
                        names.UnionWith(trialNames);result.Add(new PreparedSchedule{Request=request,View=copy,Copied=true});
                    }
                    catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                    catch(Exception ex)
                    {
                        if(stage.GetStatus()==TransactionStatus.Started)stage.RollBack();
                        warnings.Add("Спецификация "+request.Number+" «"+request.SourceName+"» пропущена: "+ex.Message);
                    }
                }
            }
            return result;
        }
        private static object WriteParameter(Document doc,Parameter parameter,string token,string raw)
        {
            switch(parameter.StorageType)
            {
                case StorageType.String:parameter.Set(token);return token;
                case StorageType.Integer:int integer=ScheduleFilterTools.Integer(token,raw);parameter.Set(integer);return integer;
                case StorageType.Double:double real=ScheduleFilterTools.Real(doc,parameter.Definition.GetDataType(),token);parameter.Set(real);return real;
                case StorageType.ElementId:var id=ScheduleFilterTools.Reference(doc,raw);parameter.Set(id);return id;
                default:throw new InvalidOperationException("Тип параметра вида не поддерживается.");
            }
        }
        private static bool ParameterMatches(Parameter p,object expected)
        {
            if(expected is string text)return p.AsString()==text;
            if(expected is int number)return p.AsInteger()==number;
            if(expected is double real)return Math.Abs(p.AsDouble()-real)<=1e-9*Math.Max(1,Math.Abs(real));
            return expected is ElementId id&&p.AsElementId()==id;
        }
        internal static SchedulePlacement Place(Document doc,ViewSheet sheet,IList<PreparedSchedule> schedules,Options options,List<string> warnings)
        {
            var result=new SchedulePlacement();if(schedules.Count==0)return result;
            var paper=sheet.Outline;
            var frame=SheetMargins.Frame(paper.Min.U,paper.Min.V,paper.Max.U,paper.Max.V);
            using(var stage=new SubTransaction(doc))
            {
                stage.Start();
                try
                {
                    var instances=new List<ScheduleSheetInstance>();var data=new List<PreparedSchedule>();var bounds=new List<PaperRect>();
                    foreach(var schedule in schedules)
                    using(var one=new SubTransaction(doc))
                    {
                        one.Start();
                        try
                        {
                            var instance=ScheduleSheetInstance.Create(doc,sheet.Id,schedule.View.Id,XYZ.Zero);doc.Regenerate();
                            var box=instance.get_BoundingBox(sheet);
                            if(box==null)throw new InvalidOperationException("Нет габаритов таблицы.");
                            var visible=ScheduleLayoutMath.VisibleBounds(new PaperRect(box.Min.X,box.Min.Y,box.Max.X-box.Min.X,box.Max.Y-box.Min.Y));
                            if(one.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Не подтверждено размещение спецификации.");
                            instances.Add(instance);data.Add(schedule);bounds.Add(visible);
                        }
                        catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                        catch(Exception ex){if(one.GetStatus()==TransactionStatus.Started)one.RollBack();warnings.Add("Спецификация "+schedule.Request.Number+" не размещена: "+ex.Message);}
                    }
                    var numbers=data.Select(s=>s.Request.Number).ToArray();var cells=options.ScheduleCells.Select(n=>numbers.Contains(n)?n:0).ToArray();
                    var positions=ScheduleLayoutMath.PackRight(bounds,numbers,cells,options.ScheduleGapMm/304.8);
                    for(int i=0;i<instances.Count;i++)
                    {
                        var b=bounds[i];var p=positions[i];
                        var shift=new XYZ(frame.Right+p.X-b.X,frame.Top-p.Y-(b.Y+b.Height),0);
                        instances[i].Point=instances[i].Point+shift;
                    }
                    doc.Regenerate();
                    if(stage.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Не подтверждена компоновка спецификаций.");
                    result.Count=instances.Count;
                    if(positions.Count>0)result.Left=frame.Right+positions.Min(p=>p.X);
                    int outside=positions.Count(p=>frame.Right+p.X<frame.Left-1e-8||p.Y+p.Height>frame.Height-55/304.8+1e-8);
                    if(outside>0)warnings.Add("Спецификации сохранены; за лист или в нижнюю зону основной надписи выходит: "+outside+". Исправьте расположение вручную.");
                }
                catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                catch(Exception ex){if(stage.GetStatus()==TransactionStatus.Started)stage.RollBack();warnings.Add("Копии спецификаций сохранены, размещение не выполнено: "+ex.Message);}
            }
            return result;
        }
    }
}
