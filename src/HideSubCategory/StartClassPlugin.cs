using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace HideCategory
{
    [Transaction(TransactionMode.Manual)]
    public class ExternalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                var window = new UserWindRoom(data.Application);
                if (window.ShowDialog() != true) return Result.Cancelled;
                TaskDialog.Show("Графика ARV", HideCategoryService.Hide(doc, window.SelectedCategoryId));
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
