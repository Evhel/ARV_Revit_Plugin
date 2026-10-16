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
            var result=new CreationResult{Selected=catalog.Elements.Count,Filtered=catalog.Elements.Count-candidates.Count};
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
                                var volume=ElementViewMath.Volume(element.Corners,element.AxisX,element.AxisY,request.Kind,request.OffsetMm/304.8,options.PaddingMm/304.8);
                                var transform=Transform.Identity;
                                transform.Origin=CatalogBuilder.Point(volume.Origin);transform.BasisX=CatalogBuilder.Point(volume.Right);
                                transform.BasisY=CatalogBuilder.Point(volume.Up);transform.BasisZ=CatalogBuilder.Point(volume.Direction);
                                var box=new BoundingBoxXYZ{Transform=transform,Min=CatalogBuilder.Point(volume.Min),Max=CatalogBuilder.Point(volume.Max),Enabled=true};
                                for(int axis=0;axis<3;axis++) {box.set_MinEnabled(axis,true);box.set_MaxEnabled(axis,true);}
                                var view=ViewSection.CreateSection(doc,new ElementId(options.SectionTypeId),box);
                                view.Name=Algorithms.UniqueName(ElementViewMath.Name(request.Prefix,token,request.Suffix),names);
                                view.ViewTemplateId=ElementId.InvalidElementId;
                                view.CropBoxActive=true;view.CropBoxVisible=false;
                                View template=null;
                                if(request.TemplateId>=0)
                                {
                                    var id=new ElementId(request.TemplateId);
                                    template=doc.GetElement(id) as View;
                                    if(template==null || !template.IsTemplate || !view.IsValidViewTemplate(id)) throw new InvalidOperationException("Несовместимый шаблон: "+request.Label+".");
                                    view.ViewTemplateId=id;
                                }
                                var scaleId=new ElementId(BuiltInParameter.VIEW_SCALE);
                                bool controlled=template!=null && template.GetTemplateParameterIds().Contains(scaleId) && !template.GetNonControlledTemplateParameterIds().Contains(scaleId);
                                if(!controlled) view.Scale=options.Scale;
                                doc.Regenerate();
                                if(!view.CropBoxActive) throw new InvalidOperationException("Шаблон отключает границы вида «"+request.Label+"». Измените шаблон.");
                                created.Add(view);
                            }
                            if(options.CreateSheet)
                            {
                                var symbol=doc.GetElement(new ElementId(options.TitleBlockId)) as FamilySymbol;
                                if(symbol==null) throw new InvalidOperationException("Не найден выбранный тип основной надписи.");
                                if(!symbol.IsActive) {symbol.Activate();doc.Regenerate();}
                                var sheet=ViewSheet.Create(doc,symbol.Id);
                                sheet.Name=ElementViewMath.Name(options.SheetPrefix,token,options.SheetSuffix);
                                int number=1;string requested;
                                do {requested="ARV-"+number.ToString("D4",CultureInfo.InvariantCulture);number++;} while(numbers.Contains(requested));
                                sheet.SheetNumber=requested;numbers.Add(requested);
                                doc.Regenerate();
                                Place(doc,sheet,created);
                            }
                            if(item.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Revit не подтвердил создание комплекта.");
                            viewNames=names;sheetNumbers=numbers;
                            result.Completed++;result.Views+=created.Count;if(options.CreateSheet) result.Sheets++;
                        }
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
        private static PaperRect Extents(Viewport viewport)
        {
            var box=viewport.GetBoxOutline();double x=box.MinimumPoint.X,y=box.MinimumPoint.Y,right=box.MaximumPoint.X,top=box.MaximumPoint.Y;
            var label=viewport.GetLabelOutline();
            if(!label.IsEmpty) {x=Math.Min(x,label.MinimumPoint.X);y=Math.Min(y,label.MinimumPoint.Y);right=Math.Max(right,label.MaximumPoint.X);top=Math.Max(top,label.MaximumPoint.Y);}
            return new PaperRect(x,y,right-x,top-y);
        }
        private static void Place(Document doc,ViewSheet sheet,IList<ViewSection> views)
        {
            var ports=new List<Viewport>();
            foreach(var view in views)
            {
                if(!Viewport.CanAddViewToSheet(doc,sheet.Id,view.Id)) throw new InvalidOperationException("Вид нельзя разместить на листе: "+view.Name);
                ports.Add(Viewport.Create(doc,sheet.Id,view.Id,XYZ.Zero));
            }
            doc.Regenerate();
            var bounds=ports.Select(Extents).ToList();
            var sheetBox=sheet.Outline;
            double margin=20/304.8,bottom=55/304.8,gap=10/304.8;
            double left=sheetBox.Min.U+margin,top=sheetBox.Max.V-margin;
            double width=sheetBox.Max.U-margin-left,height=top-(sheetBox.Min.V+bottom);
            var positions=ElementViewMath.Pack(bounds,width,height,gap);
            for(int i=0;i<ports.Count;i++)
            {
                var b=bounds[i];var target=positions[i];
                double dx=left+target.X-b.X,dy=top-target.Y-(b.Y+b.Height);
                ports[i].SetBoxCenter(ports[i].GetBoxCenter()+new XYZ(dx,dy,0));
            }
            doc.Regenerate();
            // Check final label bounds as well: a title must not overlap another view.
            var actual=ports.Select(Extents).ToList();
            for(int i=0;i<actual.Count;i++)
            {
                var a=actual[i];
                if(a.X<left-1e-6 || a.Y<sheetBox.Min.V+bottom-1e-6 || a.X+a.Width>left+width+1e-6 || a.Y+a.Height>top+1e-6)
                    throw new InvalidOperationException("После размещения вид или его название вышли за рабочую область листа.");
                for(int j=0;j<i;j++)
                {
                    var b=actual[j];
                    if(a.X<b.X+b.Width-1e-6 && a.X+a.Width>b.X+1e-6 && a.Y<b.Y+b.Height-1e-6 && a.Y+a.Height>b.Y+1e-6)
                        throw new InvalidOperationException("После размещения виды или их названия пересеклись. Увеличьте формат листа или знаменатель масштаба.");
                }
            }
        }
    }
}
