using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using System.Diagnostics;

namespace BuildinOpener
{
    public class SiteAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet categories) { return true; }
    }

    [Transaction(TransactionMode.Manual)]
    public class OpenBuildinCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string url = "https://buildin.ai/share/e29fefec-fa69-4836-9892-b0cf754b3058?code=FDRA7Z#e0c8cfa7-9668-4afc-a578-601eb9b12a9e";

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Не удалось открыть сайт:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}