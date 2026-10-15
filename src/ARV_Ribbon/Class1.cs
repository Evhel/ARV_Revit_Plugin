using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace ARV_Ribbon
{
    public class App : IExternalApplication
    {
        private static readonly string PluginsFolder = @"C:\ProgramData\Autodesk\Revit\Addins\2022\ARV";
        private const string TabName = "ARV";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Создаем вкладку
                try { application.CreateRibbonTab(TabName); }
                catch { }

                // ========== ПАНЕЛЬ "САЙТ" ==========
                RibbonPanel panelSite = application.CreateRibbonPanel(TabName, "Сайт");
                CreateButton(panelSite, "BuildinOpener.dll", "ARV.png", "ARV");

                // ========== ПАНЕЛЬ "АР" ==========
                RibbonPanel panelAR = application.CreateRibbonPanel(TabName, "АР");
                CreateButton(panelAR, "WallStartSchedule.dll", "StartWall.png", "Черновая\nотделка");
                CreateButton(panelAR, "FinishingSchedulePlugin.dll", "FinishWall.png", "Чистовая\nотделка");

                // ========== ПАНЕЛЬ "ГРАФИКА" ==========
                RibbonPanel panelGraphics = application.CreateRibbonPanel(TabName, "Графика");
                CreateButton(panelGraphics, "HideCategory.dll", "HideKat.png", "Скрыть\nкатегорию");
                CreateButton(panelGraphics, "HideSubCategory.dll", "HideSubK.png", "Скрыть\nподкатегорию");

                // ========== ПАНЕЛЬ "ВИДЫ" ==========
                RibbonPanel panelViews = application.CreateRibbonPanel(TabName, "Виды");
                CreateButton(panelViews, "CreateBlankSheetsPlugin.dll", "NewSheet.png", "Создать\nчистые листы");
                CreateButton(panelViews, "CopyViewsWithRename.dll", "CopyView.png", "Копировать\nвиды");
                CreateButton(panelViews, "PlaceViewsOnSheet.dll", "ViewsToSheets.png", "Разместить\nвиды на листах");
                CreateButton(panelViews, "TightCropPlugin.dll", "ViewsCut.png", "Обрезать\nвиды");
                CreateButton(panelViews, "ViewNumberingPlugin.dll", "ViewNumber.png", "Нумерация\nвидов");

                // ========== ПАНЕЛЬ "РАБОЧИЕ НАБОРЫ" ==========
                RibbonPanel panelWorksets = application.CreateRibbonPanel(TabName, "Рабочие наборы");
                CreateButton(panelWorksets, "CreateViewsForWorksets.dll", "Worksets.png", "3D виды на\nРабочие наборы");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }

            return Result.Succeeded;
        }

        // УНИВЕРСАЛЬНЫЙ МЕТОД ДЛЯ СОЗДАНИЯ ЛЮБОЙ КНОПКИ
        private void CreateButton(RibbonPanel panel, string dllName, string iconName, string buttonText)
        {
            string dllPath = Path.Combine(PluginsFolder, dllName);

            if (!File.Exists(dllPath))
            {
                MessageBox.Show($"Файл не найден: {dllPath}");
                return;
            }

            string className = FindCommandClass(dllPath);

            if (string.IsNullOrEmpty(className))
            {
                MessageBox.Show($"Не найден класс команды в {dllName}");
                return;
            }

            var buttonData = new PushButtonData(
                dllName.Replace(".dll", ""),
                buttonText,
                dllPath,
                className
            );

            // Добавляем иконку, если она указана
            if (!string.IsNullOrEmpty(iconName))
            {
                var icon = LoadImageFromFile(iconName);
                if (icon != null)
                {
                    buttonData.LargeImage = icon;
                    buttonData.Image = icon;
                }
            }

            // Добавляем всплывающую подсказку для кнопки логотипа
            if (dllName == "BuildinOpener.dll")
            {
                buttonData.ToolTip = "Открыть сайт ARV";
                buttonData.LongDescription = "Нажмите для перехода на сайт с документацией и обновлениями";
            }

            panel.AddItem(buttonData);
        }

        // ЗАГРУЗКА КАРТИНКИ ИЗ ПАПКИ
        private BitmapImage LoadImageFromFile(string imageName)
        {
            try
            {
                string dllFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string imagePath = Path.Combine(dllFolder, imageName);

                if (File.Exists(imagePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ПОИСК КЛАССА КОМАНДЫ В DLL
        private string FindCommandClass(string dllPath)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(dllPath);

                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(IExternalCommand).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        return type.FullName;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки {Path.GetFileName(dllPath)}: {ex.Message}");
            }

            return null;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}