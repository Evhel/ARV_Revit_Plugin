using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Diagnostics;
using System.Windows;

namespace WallStartSchedule
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                var mainWindow = new MainWindow(doc);
                bool? dialogResult = mainWindow.ShowDialog();

                if (dialogResult == true)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    var calculator = new WallStartCalculator(doc);
                    var result = calculator.CalculateAndWrite(
                        mainWindow.SelectedFinishTypes,
                        mainWindow.ColumnHeaderNames,
                        mainWindow.AreaCoefficient
                    );

                    stopwatch.Stop();

                    if (result.Success)
                    {
                        string coeffInfo = mainWindow.AreaCoefficient > 0
                            ? $"\n📈 Коэффициент запаса: {mainWindow.AreaCoefficient}%"
                            : "";

                        string statsMessage = $"✅ Готово!\n\n" +
                            $"📊 Помещений обработано: {result.RoomsCount}\n" +
                            $"🧱 Всего стен в проекте: {result.TotalWallsCount}\n" +
                            $"🎯 Стен проанализировано (выбранные типы): {result.FilteredWallsCount}" +
                            coeffInfo +
                            $"\n⏱️ Время выполнения: {stopwatch.ElapsedMilliseconds} мс";

                        MessageBox.Show(statsMessage, "Ведомость черновой отделки",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"❌ Ошибка: {result.ErrorMessage}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return Result.Failed;
            }
        }
    }
}