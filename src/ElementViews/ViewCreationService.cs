using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.Common;

namespace ARV.ElementViews
{
    internal static class ViewCreationService
    {
        internal static CreationResult Run(Document doc,Catalog catalog,Options options)
        {
            var candidates=catalog.Select(options.ParameterKey,options.Unique);
            var result=new CreationResult{Selected=catalog.Elements.Count,Filtered=catalog.Elements.Count-candidates.Count,EmptyExcluded=options.Unique?catalog.EmptyCount(options.ParameterKey):0};
            var data=catalog.Elements.ToDictionary(e=>e.Id);
            var viewNames=new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v=>v.Name),StringComparer.OrdinalIgnoreCase);
            var sheetNumbers=new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(v=>v.SheetNumber),StringComparer.OrdinalIgnoreCase);
            using(var transaction=new Transaction(doc,"ARV — создать виды элементов"))
            {
                transaction.Start();
                int serial=0;
                foreach(var candidate in candidates)
                {
                    serial++;
                    var element=data[candidate.Id];
                    var names=new HashSet<string>(viewNames,StringComparer.OrdinalIgnoreCase);
                    var numbers=new HashSet<string>(sheetNumbers,StringComparer.OrdinalIgnoreCase);
                    var warnings=new List<string>();
                    bool sheetCreated=false;
                    int scheduleCopies=0,schedulePlacements=0;
                    using(var item=new SubTransaction(doc))
                    {
                        item.Start();
                        try
                        {
                            if(doc.GetElement(new ElementId(element.Id))==null) throw new InvalidOperationException("Элемент больше не существует.");
                            string token=string.IsNullOrWhiteSpace(candidate.Value)?serial.ToString(CultureInfo.InvariantCulture):candidate.Value.Trim();
                            var created=new List<ViewSection>();
                            foreach(var request in options.Views)
                            {
                                var volume=ElementViewMath.Volume(element.Corners,element.AxisX,element.AxisY,request.Kind,request.OffsetMm/304.8,request.SideMm/304.8,request.AboveMm/304.8,request.BelowMm/304.8);
                                var transform=Transform.Identity;
                                transform.Origin=CatalogBuilder.Point(volume.Origin);transform.BasisX=CatalogBuilder.Point(volume.Right);
                                transform.BasisY=CatalogBuilder.Point(volume.Up);transform.BasisZ=CatalogBuilder.Point(volume.Direction);
                                var box=new BoundingBoxXYZ{Transform=transform,Min=CatalogBuilder.Point(volume.Min),Max=CatalogBuilder.Point(volume.Max),Enabled=true};
                                for(int axis=0;axis<3;axis++) {box.set_MinEnabled(axis,true);box.set_MaxEnabled(axis,true);}
                                var view=ViewSection.CreateSection(doc,new ElementId(options.SectionTypeId),box);
                                view.Name=Algorithms.UniqueName(ElementViewMath.Name(request.Prefix,token,request.Suffix),names);
                                view.ViewTemplateId=ElementId.InvalidElementId;
                                view.CropBoxActive=true;view.CropBoxVisible=false;
                                var id=new ElementId(request.TemplateId);
                                var template=doc.GetElement(id) as View;
                                if(request.TemplateId<=0 || template==null || !template.IsTemplate || !view.IsValidViewTemplate(id))
                                    throw new InvalidOperationException("Выберите совместимый шаблон: "+request.Label+".");
                                view.ViewTemplateId=id;
                                view.ApplyViewTemplateParameters(template);
                                doc.Regenerate();
                                if(view.Scale!=template.Scale)
                                {
                                    var scale=view.get_Parameter(BuiltInParameter.VIEW_SCALE);
                                    if(scale==null || scale.IsReadOnly) throw new InvalidOperationException("Не удалось применить масштаб шаблона: "+request.Label);
                                    view.Scale=template.Scale;
                                }
                                doc.Regenerate();
                                if(view.ViewTemplateId!=id || view.Scale!=template.Scale) throw new InvalidOperationException("Revit не сохранил шаблон и его масштаб: "+request.Label);
                                if(!view.CropBoxActive) throw new InvalidOperationException("Шаблон отключает границы вида «"+request.Label+"». Измените шаблон.");
                                string title=ElementViewMath.SheetTitle(request.TitleSuffix,token);
                                var titleParameter=view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                                if(titleParameter==null || titleParameter.IsReadOnly || !titleParameter.Set(title) || titleParameter.AsString()!=title)
                                    throw new InvalidOperationException("Не удалось заполнить «Заголовок на листе»: "+request.Label);
                                created.Add(view);
                            }
                            if(options.CreateSheet)
                            {
                                ViewSheet sheet=null;string sheetNumber=null;
                                using(var stage=new SubTransaction(doc))
                                {
                                    stage.Start();
                                    try
                                    {
                                        var symbol=doc.GetElement(new ElementId(options.TitleBlockId)) as FamilySymbol;
                                        if(symbol==null) throw new InvalidOperationException("Не найден тип основной надписи.");
                                        if(!symbol.IsActive) {symbol.Activate();doc.Regenerate();}
                                        sheet=ViewSheet.Create(doc,symbol.Id);doc.Regenerate();
                                        var blocks=new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_TitleBlocks)
                                            .WhereElementIsNotElementType().Where(e=>e.OwnerViewId==sheet.Id).ToList();
                                        SheetFormatParameters.Apply(blocks,options.FormatA,options.FormatX,()=>doc.Regenerate());
                                        doc.Regenerate();
                                        sheet.Name=ElementViewMath.Name(options.SheetPrefix,token,options.SheetSuffix);
                                        int number=1;
                                        do {sheetNumber="ARV-"+number.ToString("D4",CultureInfo.InvariantCulture);number++;} while(numbers.Contains(sheetNumber));
                                        sheet.SheetNumber=sheetNumber;doc.Regenerate();
                                        if(stage.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Не подтверждено создание листа.");
                                        sheetCreated=true;numbers.Add(sheetNumber);
                                    }
                                    catch(Autodesk.Revit.Exceptions.RegenerationFailedException) {throw;}
                                    catch(Exception ex)
                                    {
                                        if(stage.GetStatus()==TransactionStatus.Started) stage.RollBack();
                                        sheet=null;warnings.Add("Виды сохранены, лист не создан: "+ex.Message);
                                    }
                                }
                                if(sheet!=null)
                                {
                                    var schedules=ScheduleCreationService.Create(doc,options,token,candidate.GroupKey,names,warnings);
                                    scheduleCopies=schedules.Count(s=>s.Copied);
                                    var scheduleLayout=ScheduleCreationService.Place(doc,sheet,schedules,options,warnings);
                                    schedulePlacements=scheduleLayout.Count;
                                    using(var stage=new SubTransaction(doc))
                                    {
                                        stage.Start();
                                        try
                                        {
                                            int overflow=Place(doc,sheet,created,options,scheduleLayout.Left);
                                            if(stage.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Не подтверждено размещение.");
                                            if(overflow>0)warnings.Add("Виды и лист сохранены. За полезную область или в зону спецификаций выходит видов: "+overflow+". Расположение по схеме сохранено; поправьте компоновку вручную.");
                                        }
                                        catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                                        catch(Exception ex)
                                        {
                                            if(stage.GetStatus()==TransactionStatus.Started)stage.RollBack();
                                            warnings.Add("Виды, лист и спецификации сохранены; виды не размещены: "+ex.Message);
                                        }
                                    }
                                }
                            }
                            for(int index=0;index<created.Count;index++)
                                if(created[index].ViewTemplateId.IntegerValue!=options.Views[index].TemplateId)
                                    throw new InvalidOperationException("После обработки потерян шаблон вида: "+created[index].Name);
                            if(item.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Revit не подтвердил создание комплекта.");
                            viewNames=names;sheetNumbers=numbers;
                            result.ScheduleCopies+=scheduleCopies;result.SchedulePlacements+=schedulePlacements;
                            result.Completed++;result.Views+=created.Count;if(sheetCreated) result.Sheets++;
                            if(warnings.Count>0) result.Warnings.Add(new SkippedElement{Element=element.Id+" — "+element.Name,Reason=string.Join(" ",warnings)});
                        }
                        catch(Autodesk.Revit.Exceptions.RegenerationFailedException) {throw;}
                        catch(Exception ex)
                        {
                            if(item.GetStatus()==TransactionStatus.Started) item.RollBack();
                            result.Skipped.Add(new SkippedElement{Element=element.Id+" — "+element.Name,Reason=ex.Message});
                        }
                    }
                }
                if(result.Completed>0) RevitSupport.Commit(transaction);else transaction.RollBack();
            }
            return result;
        }
        private sealed class PlacedView
        {
            internal Viewport Port;
        }
        private static PaperRect Extents(PlacedView placed)
        {
            var box=placed.Port.GetBoxOutline();double x=box.MinimumPoint.X,y=box.MinimumPoint.Y,right=box.MaximumPoint.X,top=box.MaximumPoint.Y;
            var label=placed.Port.GetLabelOutline();
            if(!label.IsEmpty) {x=Math.Min(x,label.MinimumPoint.X);y=Math.Min(y,label.MinimumPoint.Y);right=Math.Max(right,label.MaximumPoint.X);top=Math.Max(top,label.MaximumPoint.Y);}
            return ElementViewMath.FrameAlignedBounds(new PaperRect(x,y,right-x,top-y),placed.Port.GetBoxCenter().X);
        }
        private static int Place(Document doc,ViewSheet sheet,IList<ViewSection> views,Options options,double? schedulesLeft)
        {
            if(views.Count==0)return 0;
            var ports=new List<PlacedView>();
            for(int index=0;index<views.Count;index++)
            {
                var view=views[index];
                if(!Viewport.CanAddViewToSheet(doc,sheet.Id,view.Id)) throw new InvalidOperationException("Вид нельзя разместить: "+view.Name);
                var port=Viewport.Create(doc,sheet.Id,view.Id,XYZ.Zero);
                int typeId=options.Views[index].ViewportTypeId;
                if(typeId>0)
                {
                    var id=new ElementId(typeId);
                    if(!port.GetValidTypes().Contains(id)) throw new InvalidOperationException("Недопустимый тип видового экрана: "+view.Name);
                    port.ChangeTypeId(id);
                    if(port.GetTypeId()!=id) throw new InvalidOperationException("Не применён тип видового экрана.");
                }
                ports.Add(new PlacedView{Port=port});
            }
            doc.Regenerate();
            var bounds=ports.Select(port=>Extents(port)).ToList();
            var sheetBox=sheet.Outline;
            var frame=SheetMargins.Frame(sheetBox.Min.U,sheetBox.Min.V,sheetBox.Max.U,sheetBox.Max.V);
            double bottom=frame.Bottom+55/304.8,gap=options.SheetGapMm/304.8;
            double left=frame.Left+options.SheetLeftMm/304.8,top=frame.Top-options.SheetTopMm/304.8;
            double right=schedulesLeft.HasValue?Math.Min(frame.Right,schedulesLeft.Value-gap):frame.Right;
            double width=right-left,height=top-bottom;
            var positions=options.UseLayout
                ?ElementViewMath.PackByContent(bounds,options.Views.Select(v=>v.Number).ToArray(),options.LayoutCells,options.LayoutRows,options.LayoutColumns,gap)
                :ElementViewMath.PackPreserving(bounds,width,height,gap,sheetBox.Max.U-left+gap);
            int overflow=positions.Count(position=>position.X+position.Width>width+1e-8 || position.Y+position.Height>height+1e-8);
            for(int i=0;i<ports.Count;i++)
            {
                var b=bounds[i];var target=positions[i];
                var shift=new XYZ(left+target.X-b.X,top-target.Y-(b.Y+b.Height),0);
                ports[i].Port.SetBoxCenter(ports[i].Port.GetBoxCenter()+shift);
            }
            doc.Regenerate();
            return overflow;
        }
    }
}
