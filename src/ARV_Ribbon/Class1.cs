using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace ARV_Ribbon
{
    public class App : IExternalApplication
    {
        private const string TabName = "ARV";
        private static readonly string PluginsFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public Result OnStartup(UIControlledApplication app)
        {
            var errors = new List<string>();
            int added = 0;
            try
            {
                try { app.CreateRibbonTab(TabName); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { /* Вкладка уже существует. */ }
                added += Add(app, "Сайт", "BuildinOpener", "BuildinOpener.OpenBuildinCommand", "ARV.png", "ARV", "Открыть документацию ARV", errors, "BuildinOpener.SiteAvailability");
                added += Add(app, "АР", "WallStartSchedule", "WallStartSchedule.Command", "StartWall.png", "Черновая\nотделка", "Записать черновую отделку в параметры помещений", errors);
                added += Add(app, "АР", "FinishingSchedulePlugin", "FinishingSchedulePlugin.Command", "FinishWall.png", "Чистовая\nотделка", "Записать чистовую отделку в параметры помещений", errors);
                added += Add(app, "Графика", "HideCategory", "HideCategory.ExternalCommand", "HideKat.png", "Скрыть\nкатегорию", "Скрыть категорию во всех подходящих видах и шаблонах", errors);
                added += Add(app, "Графика", "HideSubCategory", "HideCategory.ExternalCommand", "HideSubK.png", "Скрыть\nподкатегорию", "Скрыть подкатегорию во всех подходящих видах и шаблонах", errors);
                added += Add(app, "Виды", "CreateBlankSheetsPlugin", "CreateBlankSheetsPlugin.CreateBlankSheetsCommand", "NewSheet.png", "Создать\nчистые листы", "Создать листы с выбранной основной надписью", errors);
                added += Add(app, "Виды", "CopyViewsWithRename", "CopyViewsWithRename.CopyViewsCommand", "CopyView.png", "Копировать\nвиды", "Копировать выбранные виды с переименованием", errors);
                added += Add(app, "Виды", "PlaceViewsOnSheet", "PlaceViewsOnSheet.PlaceViewsCommand", "ViewsToSheets.png", "Виды\nна лист", "Разместить выбранные виды по алфавиту параметра «Имя вида»", errors);
                added += Add(app, "Виды", "TightCropPlugin", "AdjustCropToBreakLines.AdjustCropToBreakLinesCommand", "ViewsCut.png", "Обрезать\nвиды", "Подогнать прямоугольную обрезку без изменения глубины", errors);
                added += Add(app, "Виды", "ViewNumberingPlugin", "ViewNumberingPlugin.Command", "ViewNumber.png", "Нумерация\nвидов", "Нумеровать выбранные видовые экраны по положению на листе", errors);
                added += Add(app, "Рабочие наборы", "CreateViewsForWorksets", "WorksetViewsPlugin.WorksetViewsCommand", "Worksets.png", "3D виды по\nрабочим наборам", "Создать отдельный 3D-вид для каждого пользовательского рабочего набора", errors);
                added += Add(app, "Модель", "Arv.OriginRelocator", "Arv.OriginRelocator.MoveToInternalOriginCommand", "OriginToZero.png", "Перенос\nв 0,0", "Перенести выбранную точку проекта во внутренний 0,0 по X и Y", errors);
                added += Add(app, "Армирование", "AreaReinforcementHosts", "ARV.AreaReinforcementHosts.RehostSingleCommand", "RehostArea.png", "Новая\nоснова АП", "Выбрать армирование по площади и назначить новую основу", errors);
                added += Add(app, "Армирование", "AreaReinforcementHosts", "ARV.AreaReinforcementHosts.RehostByFloorCommand", "RehostAreasFloor.png", "Поменять этаж\nоснов АП", "Назначить основы выбранным армированиям по нижней границе контура на выбранном этаже", errors);
            }
            catch (Exception ex) { errors.Add(ex.Message); }
            if (errors.Count > 0) TaskDialog.Show("Панель ARV", $"Создано кнопок: {added} из 14.\n" + string.Join("\n", errors));
            return added == 0 ? Result.Failed : Result.Succeeded;
        }

        private static int Add(UIControlledApplication app, string panelName, string assembly, string command,
            string iconName, string label, string tooltip, List<string> errors,
            string availability = "ARV.Common.ProjectCommandAvailability")
        {
            try
            {
                string path = Path.Combine(PluginsFolder, assembly + ".dll");
                if (!File.Exists(path)) throw new FileNotFoundException("Отсутствует DLL", path);
                var panel = app.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == panelName) ?? app.CreateRibbonPanel(TabName, panelName);
                var data = new PushButtonData(assembly + "." + command, label, path, command) { ToolTip = tooltip, AvailabilityClassName = availability };
                string icon = Path.Combine(PluginsFolder, iconName);
                if (!File.Exists(icon)) icon = Path.Combine(PluginsFolder, "Images", iconName);
                if (File.Exists(icon))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(icon); bitmap.EndInit(); bitmap.Freeze();
                        data.LargeImage = bitmap; data.Image = bitmap;
                    }
                    catch (Exception ex) { errors.Add(iconName + ": " + ex.Message); }
                }
                else errors.Add("Не найдена иконка " + iconName);
                panel.AddItem(data);
                return 1;
            }
            catch (Exception ex) { errors.Add(assembly + ": " + ex.Message); return 0; }
        }
        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }
    }
}
