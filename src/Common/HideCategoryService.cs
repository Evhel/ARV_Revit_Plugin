using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ARV.Common
{
    public static class HideCategoryService
    {
        public static string Hide(Document doc, ElementId categoryId)
        {
            int changed = 0, alreadyHidden = 0;
            var errors = new List<string>();
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .OrderByDescending(v => v.IsTemplate).ThenBy(v => v.Name, Algorithms.NameComparer).ToList();
            using (var transaction = new Transaction(doc, "Скрыть категорию / подкатегорию"))
            {
                transaction.Start();
                foreach (var view in views)
                {
                    if (!view.CanCategoryBeHidden(categoryId)) continue;
                    if (view.GetCategoryHidden(categoryId)) { alreadyHidden++; continue; }
                    using (var item = new SubTransaction(doc))
                    {
                        item.Start();
                        try
                        {
                            // Шаблоны обработаны первыми. Никогда не снимаем шаблон с вида.
                            view.SetCategoryHidden(categoryId, true);
                            if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Изменение не подтверждено.");
                            changed++;
                        }
                        catch (Exception ex)
                        {
                            if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                            errors.Add(view.Name + ": " + ex.Message);
                        }
                    }
                }
                RevitSupport.Commit(transaction);
            }
            return $"Изменено видов/шаблонов: {changed}. Уже скрыто: {alreadyHidden}. Ошибок: {errors.Count}.\n" + string.Join("\n", errors);
        }
    }
}
