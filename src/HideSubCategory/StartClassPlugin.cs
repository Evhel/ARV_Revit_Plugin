using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using System.CodeDom;
using Autodesk.Revit.DB.Architecture;

namespace HideCategory
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExternalCommand : IExternalCommand
    {
        public static ExternalEvent exEvent;
        public static HideCategoryHandler handler;
        public static string CategoryNameToHide;
        public static string SubCategoryNameToHide;

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
            var categories = doc.Settings.Categories;

            ElementId targetId = null;

            // Ищем выбранную категорию
            Category selectedCategory = categories.Cast<Category>()
                .FirstOrDefault(cat => cat.Name.Equals(ExternalCommand.CategoryNameToHide, StringComparison.OrdinalIgnoreCase));

            if (selectedCategory != null && selectedCategory.SubCategories != null)
            {
                // Ищем подкатегорию внутри выбранной категории
                var subCat = selectedCategory.SubCategories.Cast<Category>()
                    .FirstOrDefault(sc => sc.Name.Equals(ExternalCommand.SubCategoryNameToHide, StringComparison.OrdinalIgnoreCase));

                if (subCat != null)
                {
                    targetId = subCat.Id;
                }
            }

            if (targetId == null)
            {
                MessageBox.Show($"Подкатегория '{ExternalCommand.SubCategoryNameToHide}' не найдена в категории '{ExternalCommand.CategoryNameToHide}'");
                return;
            }

            using (Transaction t = new Transaction(doc, "Скрыть подкатегорию"))
            {
                t.Start();

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .WhereElementIsNotElementType()
                    .Cast<View>();

                int hiddenCount = 0;

                foreach (var view in views)
                {
                    if (!view.CanCategoryBeHidden(targetId))
                        continue;

                    if (view.IsTemplate)
                    {
                        view.SetCategoryHidden(targetId, true);
                        hiddenCount++;
                    }
                    else
                    {
                        if (view.ViewTemplateId == ElementId.InvalidElementId)
                        {
                            view.SetCategoryHidden(targetId, true);
                            hiddenCount++;
                        }
                        else
                        {
                            var originalTemplateId = view.ViewTemplateId;
                            view.ViewTemplateId = ElementId.InvalidElementId;
                            view.SetCategoryHidden(targetId, true);
                            view.ViewTemplateId = originalTemplateId;
                            hiddenCount++;
                        }
                    }
                }

                t.Commit();
                MessageBox.Show($"Подкатегория '{ExternalCommand.SubCategoryNameToHide}' успешно скрыта в {hiddenCount} видах");
            }
        }

        public string GetName()
        {
            return "Hide Category Handler";
        }
    }
}