using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARV.Common;

namespace CreateBlankSheetsPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CreateBlankSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (!RevitSupport.TryProject(data, out UIDocument uidoc, out Document doc)) return Result.Cancelled;
                var titleBlocks = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>().OrderBy(t => t.FamilyName, Algorithms.NameComparer).ThenBy(t => t.Name, Algorithms.NameComparer).ToList();
                if (titleBlocks.Count == 0)
                {
                    TaskDialog.Show("Создать листы", "Загрузите семейство основной надписи в проект.");
                    return Result.Cancelled;
                }
                var window = new MainWindow(data.Application, titleBlocks);
                if (window.ShowDialog() != true) return Result.Cancelled;
                using (var transaction = new Transaction(doc, "Создать чистые листы"))
                {
                    transaction.Start();
                    var symbol = (FamilySymbol)doc.GetElement(window.SelectedTitleBlockId);
                    if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
                    for (int i = 0; i < window.SheetCount; i++) ViewSheet.Create(doc, symbol.Id);
                    RevitSupport.Commit(transaction);
                }
                TaskDialog.Show("Создать листы", $"Создано листов: {window.SheetCount}.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
