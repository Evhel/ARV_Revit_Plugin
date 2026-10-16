using System;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Arv.OriginRelocator
{
#if !SECTION_PROBE
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MoveToInternalOriginCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null) { TaskDialog.Show("ARV", "Откройте проект Revit."); return Result.Cancelled; }
            Document doc = ui.Document;
            if (doc.IsFamilyDocument || doc.IsReadOnly || doc.IsModifiable)
            {
                TaskDialog.Show("ARV", "Команда работает в проекте вне режима редактирования.");
                return Result.Cancelled;
            }
            if (data.Application.Application.VersionNumber != "2022")
            { message = "DLL предназначена для Revit 2022."; return Result.Failed; }
            View active = doc.ActiveView;
            ViewPlan planView = active as ViewPlan;
            if (planView == null || active.IsTemplate)
            {
                TaskDialog.Show("ARV", "Откройте план этажа, потолка или инженерный план и запустите команду ещё раз.");
                return Result.Cancelled;
            }
            RelocationReport report = new RelocationReport(doc.Title);
            try
            {
                XYZ point = ui.Selection.PickPoint(ObjectSnapTypes.Intersections | ObjectSnapTypes.Endpoints |
                    ObjectSnapTypes.Nearest, "Выберите пересечение осей, которое должно оказаться во внутреннем 0,0. Esc — отмена.");
                XYZ delta = new XYZ(-point.X, -point.Y, 0);
                if (delta.GetLength() < RelocationEngine.Tolerance)
                { TaskDialog.Show("ARV", "Выбранная точка уже находится во внутреннем 0,0."); return Result.Cancelled; }

                RelocationPlan plan = RelocationPlan.Create(doc, report);
                var dialog = new RelocationWindow(delta.X * 304.8, delta.Y * 304.8,
                    plan.Models.Count, plan.ModelRoots.Count, plan.Views.Count, plan.Annotations.Count);
                new System.Windows.Interop.WindowInteropHelper(dialog) { Owner = data.Application.MainWindowHandle };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                RelocationEngine.Execute(doc, plan, delta, report);
                string path = report.TrySave();
                TaskDialog.Show("ARV — перенос выполнен",
                    "Выбранная точка совмещена с внутренним 0,0. Высоты сохранены.\n" +
                    "Закрепление восстановлено; связи проверены.\n\nОтчёт: " + path);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                report.Lines.Add("ОШИБКА: " + ex);
                string path = report.TrySave();
                message = ex.Message;
                TaskDialog.Show("ARV — перенос отменён",
                    "Перенос не завершён. Изменения операции отменены.\n\n" + ex.Message +
                    "\n\nОтчёт: " + path);
                return Result.Failed;
            }
        }
    }

#endif
    internal sealed class RelocationReport
    {
        public readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
        public RelocationReport(string title)
        {
            Lines.Add("ARV Origin Relocator " + typeof(RelocationReport).Assembly.GetName().Version + " / Revit 2022");
            Lines.Add("UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Lines.Add("Документ: " + title);
        }
        public string Save()
        {
            // Prefer the package folder; fall back to LocalAppData when installed in a read-only folder.
            string folder = Path.Combine(Path.GetDirectoryName(typeof(RelocationReport).Assembly.Location), "reports");
            try { return Write(folder); }
            catch (IOException) { return Write(FallbackFolder()); }
            catch (UnauthorizedAccessException) { return Write(FallbackFolder()); }
        }
        private static string FallbackFolder()
        { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARV", "OriginRelocator", "reports"); }
        private string Write(string folder)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "relocation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            File.WriteAllLines(path, Lines, new UTF8Encoding(true));
            return path;
        }
        public string TrySave() { try { return Save(); } catch (Exception e) { return "Не удалось записать отчёт: " + e.Message; } }
        public static string Describe(Element e)
        { return e == null ? "(элемент отсутствует)" : e.Id.IntegerValue + " / " + e.GetType().Name + " / " + e.Name; }
    }
}
