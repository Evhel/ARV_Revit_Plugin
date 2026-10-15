using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CreateBlankSheetsPlugin
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateBlankSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Отсутствует активный документ.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument)
            {
                message = "Плагин работает только в проектах (не в семействах).";
                return Result.Failed;
            }

            // Получаем активные типы штампов
            var titleBlocks = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .Where(tb => tb.IsActive)
                .OrderBy(tb => tb.Name)
                .ToList();

            if (!titleBlocks.Any())
            {
                message = "В проекте не найдены активные типы основных надписей. Загрузите или активируйте штамп в проекте.";
                return Result.Failed;
            }

            // Создаём окно, передаём UIApplication для центрирования
            var window = new MainWindow(commandData.Application, titleBlocks);
            bool? dialogResult = window.ShowDialog();

            if (dialogResult == true)
            {
                try
                {
                    using (Transaction t = new Transaction(doc, "Создать пустые листы"))
                    {
                        t.Start();
                        for (int i = 0; i < window.SheetCount; i++)
                        {
                            ViewSheet.Create(doc, window.SelectedTitleBlockId);
                        }
                        t.Commit();
                    }
                    TaskDialog.Show("Успех", $"Создано {window.SheetCount} пустых листов.");
                    return Result.Succeeded;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                catch (System.Exception ex)
                {
                    message = $"Ошибка при создании листов: {ex.Message}";
                    return Result.Failed;
                }
            }

            return Result.Cancelled;
        }
    }
}