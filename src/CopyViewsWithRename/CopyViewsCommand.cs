using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CopyViewsWithRename
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CopyViewsCommand : IExternalCommand
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
                // Получаем выбранные виды в диспетчере проекта
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();

                if (selectedIds.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Пожалуйста, выберите виды в диспетчере проекта перед запуском плагина.");
                    return Result.Failed;
                }

                // Фильтруем только виды (исключаем шаблоны видов)
                List<View> selectedViews = new List<View>();
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem is View view && !view.IsTemplate)
                    {
                        // Проверяем, можно ли копировать этот тип вида
                        // Исключаем некоторые типы видов, которые нельзя копировать
                        if (view.ViewType != ViewType.ThreeD &&
                            view.ViewType != ViewType.Legend &&
                            view.ViewType != ViewType.Schedule &&
                            view.ViewType != ViewType.DrawingSheet)
                        {
                            selectedViews.Add(view);
                        }
                        else
                        {
                            TaskDialog.Show("Предупреждение", $"Вид '{view.Name}' типа '{view.ViewType}' не может быть скопирован и будет пропущен.");
                        }
                    }
                }

                if (selectedViews.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Нет доступных для копирования видов.");
                    return Result.Failed;
                }

                // Открываем WPF окно
                CopyViewsWindow window = new CopyViewsWindow(selectedViews);
                bool? dialogResult = window.ShowDialog();

                if (dialogResult == true)
                {
                    int copiesCount = window.CopiesCount;
                    string findText = window.FindText;
                    string replaceText = window.ReplaceText;
                    bool useReplace = window.UseReplace;
                    ViewDuplicateOption duplicateOption = window.DuplicateOption;

                    int totalCopies = 0;
                    List<string> errors = new List<string>();

                    using (Transaction trans = new Transaction(doc, "Копирование видов"))
                    {
                        trans.Start();

                        foreach (View sourceView in selectedViews)
                        {
                            for (int i = 1; i <= copiesCount; i++)
                            {
                                try
                                {
                                    // Формируем новое имя
                                    string newName = sourceView.Name;

                                    if (useReplace && !string.IsNullOrEmpty(findText))
                                    {
                                        newName = newName.Replace(findText, replaceText);
                                    }

                                    // Добавляем суффикс
                                    if (copiesCount > 1 || !useReplace)
                                    {
                                        newName = $"{newName}_копия{i}";
                                    }
                                    else
                                    {
                                        newName = $"{newName}_{i}";
                                    }

                                    // Проверяем, существует ли уже вид с таким именем
                                    FilteredElementCollector collector = new FilteredElementCollector(doc);
                                    View existingView = collector.OfClass(typeof(View))
                                        .Cast<View>()
                                        .FirstOrDefault(v => v.Name == newName && !v.IsTemplate);

                                    if (existingView != null)
                                    {
                                        newName = $"{newName}_{DateTime.Now.Ticks}";
                                    }

                                    // Копируем вид с выбранным типом копирования
                                    ElementId newViewId = sourceView.Duplicate(duplicateOption);
                                    View newView = doc.GetElement(newViewId) as View;
                                    newView.Name = newName;
                                    totalCopies++;
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"Ошибка при копировании '{sourceView.Name}': {ex.Message}");
                                }
                            }
                        }

                        trans.Commit();
                    }

                    // Показываем результат
                    string resultMessage = $"Успешно создано {totalCopies} копий видов.";
                    if (errors.Count > 0)
                    {
                        resultMessage += $"\n\nОшибки:\n{string.Join("\n", errors)}";
                    }

                    TaskDialog.Show("Результат", resultMessage);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}