using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using ARV.Common;

namespace ARV.ElementViews
{
    internal static class CatalogBuilder
    {
        internal static Point3 Point(XYZ p)=>new Point3(p.X,p.Y,p.Z);
        internal static XYZ Point(Point3 p)=>new XYZ(p.X,p.Y,p.Z);
        internal static bool IsSupported(Element e)
        {
            return e!=null && !(e is ElementType) && !e.ViewSpecific && e.Category!=null &&
                e.Category.CategoryType==CategoryType.Model && !(e is RevitLinkInstance) && e.get_BoundingBox(null)!=null;
        }
        internal static Catalog Build(Document doc,IList<Element> selected)
        {
            var result=new Catalog();var parameters=new Dictionary<string,Choice>();
            foreach(var e in selected)
            {
                if(!IsSupported(e)) {result.UnsupportedCount++;continue;}
                var box=e.get_BoundingBox(null);var points=new List<Point3>();
                foreach(double x in new[]{box.Min.X,box.Max.X}) foreach(double y in new[]{box.Min.Y,box.Max.Y}) foreach(double z in new[]{box.Min.Z,box.Max.Z})
                    points.Add(Point(box.Transform.OfPoint(new XYZ(x,y,z))));
                var item=new ElementData {Id=e.Id.IntegerValue,Name=(e.Category.Name+": "+e.Name),Corners=points.ToArray(),AxisX=new Point3(1,0,0),AxisY=new Point3(0,1,0),OrientationNote="Оси проекта: у элемента нет локальных осей."};
                var family=e as FamilyInstance;
                var location=e.Location as LocationCurve;
                XYZ axis=null, facing=null;
                if(family!=null) {var transform=family.GetTransform();axis=transform.BasisX;facing=transform.BasisY;item.OrientationNote="Локальные оси семейства.";}
                else if(location!=null) {axis=location.Curve.ComputeDerivatives(0.5,true).BasisX;facing=XYZ.BasisZ.CrossProduct(axis);item.OrientationNote="Локальная X вдоль линии элемента.";}
                if(axis!=null && new XYZ(axis.X,axis.Y,0).GetLength()>1e-8) {item.AxisX=Point(axis);item.AxisY=Point(facing);}
                else if(axis!=null) item.OrientationNote="Локальная X вертикальна: применены оси проекта.";
                ReadParameters(e,false,item,parameters);
                var type=doc.GetElement(e.GetTypeId());if(type!=null) ReadParameters(type,true,item,parameters);
                result.Elements.Add(item);
            }
            result.Parameters.Add(new Choice {Key="",Id=-1,Name="Порядковый номер (без параметра)"});
            result.Parameters.AddRange(parameters.Values.OrderBy(p=>p.Name,Algorithms.NameComparer).ThenBy(p=>p.Key));
            result.SectionTypes=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Where(t=>t.ViewFamily==ViewFamily.Section)
                .OrderBy(t=>t.Name,Algorithms.NameComparer).Select(t=>new Choice{Id=t.Id.IntegerValue,Name=t.Name}).ToList();
            result.Templates.Add(new Choice{Id=-1,Name="Без шаблона"});
            result.Templates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v=>v.IsTemplate && v.ViewType==ViewType.Section)
                .OrderBy(v=>v.Name,Algorithms.NameComparer).Select(v=>new Choice{Id=v.Id.IntegerValue,Name=v.Name}));
            result.TitleBlocks=new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>()
                .OrderBy(t=>t.FamilyName,Algorithms.NameComparer).ThenBy(t=>t.Name,Algorithms.NameComparer).Select(t=>new Choice{Id=t.Id.IntegerValue,Name=t.FamilyName+": "+t.Name}).ToList();
            return result;
        }
        private static void ReadParameters(Element e,bool type,ElementData item,Dictionary<string,Choice> choices)
        {
            foreach(Parameter p in e.Parameters)
            {
                if(p.StorageType==StorageType.None || p.Definition==null) continue;
                string key=(type?"T:":"I:")+p.Id.IntegerValue;
                if(!choices.ContainsKey(key)) choices.Add(key,new Choice{Key=key,Id=p.Id.IntegerValue,Name=p.Definition.Name+(type?" (тип)":" (экземпляр)")+" ["+p.Id.IntegerValue+"]"});
                string text="",group="";
                if(p.HasValue)
                {
                    switch(p.StorageType)
                    {
                        case StorageType.String: text=p.AsString()??"";group="S:"+text.Trim();break;
                        case StorageType.Integer: group="I:"+p.AsInteger().ToString(CultureInfo.InvariantCulture);text=p.AsValueString()??p.AsInteger().ToString(CultureInfo.InvariantCulture);break;
                        case StorageType.Double: group="D:"+p.AsDouble().ToString("R",CultureInfo.InvariantCulture);text=p.AsValueString()??p.AsDouble().ToString("R",CultureInfo.InvariantCulture);break;
                        case StorageType.ElementId:
                            var id=p.AsElementId();
                            if(id!=ElementId.InvalidElementId) {group="E:"+id.IntegerValue;var referenced=e.Document.GetElement(id);text=referenced?.Name??p.AsValueString()??id.IntegerValue.ToString(CultureInfo.InvariantCulture);}
                            break;
                    }
                }
                item.Values[key]=text;item.GroupKeys[key]=group;
            }
        }
    }
}
