using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HideCategory
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExternalCommand : IExternalCommand
    {
        public static ExternalEvent exEvent;
        public static HideCategoryHandler handler;
        public static ElementId CategoryIdToHide;

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            handler = new HideCategoryHandler(commandData.Application);
            exEvent = ExternalEvent.Create(handler);

            var window = new UserWindRoom(commandData.Application);
            window.ShowDialog();

            return Result.Succeeded;
        }
    }

    public class HideCategoryHandler : IExternalEventHandler
    {
        private UIApplication uiapp;

        public HideCategoryHandler(UIApplication app)
        {
            uiapp = app;
        }

        public void Execute(UIApplication app)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            if (ExternalCommand.CategoryIdToHide == null || ExternalCommand.CategoryIdToHide == ElementId.InvalidElementId)
            {
                return;
            }

            using (Transaction t = new Transaction(doc, "Скрыть категорию"))
            {
                t.Start();

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .WhereElementIsNotElementType()
                    .Cast<View>();

                int processedCount = 0;

                foreach (var view in views)
                {
                    if (!view.CanCategoryBeHidden(ExternalCommand.CategoryIdToHide))
                        continue;

                    try
                    {
                        if (view.IsTemplate)
                        {
                            view.SetCategoryHidden(ExternalCommand.CategoryIdToHide, true);
                        }
                        else
                        {
                            if (view.ViewTemplateId == ElementId.InvalidElementId)
                            {
                                view.SetCategoryHidden(ExternalCommand.CategoryIdToHide, true);
                            }
                            else
                            {
                                var originalTemplateId = view.ViewTemplateId;
                                view.ViewTemplateId = ElementId.InvalidElementId;
                                view.SetCategoryHidden(ExternalCommand.CategoryIdToHide, true);
                                view.ViewTemplateId = originalTemplateId;
                            }
                        }
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        // Логируем ошибку для конкретного вида, но продолжаем
                        System.Diagnostics.Debug.WriteLine($"Ошибка при обработке вида {view.Name}: {ex.Message}");
                    }
                }

                t.Commit();

                // Показываем результат
                TaskDialog.Show("Выполнено",
                    $"Категория успешно скрыта во всех видах.\n\n" +
                    $"Обработано видов: {processedCount}");
            }
        }

        public string GetName()
        {
            return "Hide Category Handler";
        }
    }
}