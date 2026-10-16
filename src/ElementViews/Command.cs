using System;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ARV.Common;

namespace ARV.ElementViews
{
    internal sealed class ModelFilter : ISelectionFilter
    {
        public bool AllowElement(Element e)=>CatalogBuilder.IsSupported(e);
        public bool AllowReference(Reference reference,XYZ point)=>false;
    }
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            try
            {
                if(!RevitSupport.TryProject(data,out UIDocument ui,out Document doc)) return Result.Cancelled;
                var selected=ui.Selection.GetElementIds().Select(doc.GetElement).ToList();
                if(!selected.Any(CatalogBuilder.IsSupported))
                    selected=ui.Selection.PickObjects(ObjectType.Element,new ModelFilter(),"Выберите элементы для создания видов и нажмите «Готово». Esc — отмена.")
                        .Select(doc.GetElement).GroupBy(e=>e.Id.IntegerValue).Select(g=>g.First()).ToList();
                var catalog=CatalogBuilder.Build(doc,selected);
                if(catalog.Elements.Count==0) {TaskDialog.Show("ARV — Виды элемента","Нет подходящих элементов модели.");return Result.Cancelled;}
                if(catalog.SectionTypes.Count==0) {TaskDialog.Show("ARV — Виды элемента","В проекте нет типа вида семейства «Разрез».");return Result.Cancelled;}
                var dialog=new MainWindow(catalog){TitleBlockDiagnosticsReader=id=>TitleBlockDiagnostics.Read(doc,id)};new WindowInteropHelper(dialog){Owner=data.Application.MainWindowHandle};
                if(dialog.ShowDialog()!=true) return Result.Cancelled;
                var result=ViewCreationService.Run(doc,catalog,dialog.Settings);
                var report=new ReportWindow(result);new WindowInteropHelper(report){Owner=data.Application.MainWindowHandle};report.ShowDialog();
                return Result.Succeeded;
            }
            catch(Autodesk.Revit.Exceptions.OperationCanceledException){return Result.Cancelled;}
            catch(Exception ex){message=ex.Message;return Result.Failed;}
        }
    }
}
