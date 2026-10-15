using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace WorksetViewsPlugin
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetViewsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Получаем все пользовательские рабочие наборы
                FilteredWorksetCollector collector = new FilteredWorksetCollector(doc);
                var userWorksets = collector.ToWorksets()
                    .Where(ws => ws.Kind == WorksetKind.UserWorkset)
                    .ToList();

                if (userWorksets.Count == 0)
                {
                    TaskDialog.Show("Внимание", "В проекте нет пользовательских рабочих наборов.");
                    return Result.Succeeded;
                }

                using (Transaction trans = new Transaction(doc, "Создание 3D видов по рабочим наборам"))
                {
                    trans.Start();

                    // Получаем тип семейства для 3D вида
                    ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                    if (viewFamilyType == null)
                    {
                        TaskDialog.Show("Ошибка", "Не найден тип семейства для 3D вида.");
                        return Result.Failed;
                    }

                    foreach (Workset ws in userWorksets)
                    {
                        // Создаём новый 3D вид с префиксом "РН_"
                        View3D view3D = View3D.CreateIsometric(doc, viewFamilyType.Id);
                        view3D.Name = $"РН_{ws.Name}";

                        // Включаем все категории модели
                        EnableAllCategories(doc, view3D);

                        // Управляем видимостью через вкладку "Рабочие наборы" в V/G
                        SetWorksetVisibilityInVG(view3D, ws.Id, doc);
                    }

                    trans.Commit();
                }

                TaskDialog.Show("Готово", $"Создано {userWorksets.Count} 3D видов.\n\nПрефикс 'РН_' добавлен к названиям видов.\nНа каждом виде настроена вкладка 'Рабочие наборы' в V/G.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", ex.Message);
                return Result.Failed;
            }
        }

        private void EnableAllCategories(Document doc, View3D view)
        {
            // Включаем все категории модели
            Categories categories = doc.Settings.Categories;

            foreach (Category category in categories)
            {
                try
                {
                    view.SetCategoryHidden(category.Id, false);
                }
                catch
                {
                    continue;
                }
            }
        }

        private void SetWorksetVisibilityInVG(View3D view, WorksetId worksetIdToShow, Document doc)
        {
            // Получаем все рабочие наборы
            FilteredWorksetCollector collector = new FilteredWorksetCollector(doc);
            ICollection<Workset> allWorksets = collector.ToWorksets();

            // Настраиваем видимость каждого рабочего набора
            foreach (Workset ws in allWorksets)
            {
                // Работаем только с пользовательскими рабочими наборами
                if (ws.Kind == WorksetKind.UserWorkset)
                {
                    if (ws.Id.IntegerValue == worksetIdToShow.IntegerValue)
                    {
                        // Показываем выбранный рабочий набор
                        view.SetWorksetVisibility(ws.Id, WorksetVisibility.Visible);
                    }
                    else
                    {
                        // Скрываем все остальные рабочие наборы
                        view.SetWorksetVisibility(ws.Id, WorksetVisibility.Hidden);
                    }
                }
                else
                {
                    // Служебные рабочие наборы тоже скрываем
                    view.SetWorksetVisibility(ws.Id, WorksetVisibility.Hidden);
                }
            }
        }
    }
}