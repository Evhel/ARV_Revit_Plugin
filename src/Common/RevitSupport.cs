using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ARV.Common
{
    public class ProjectCommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet categories)
        {
            var doc = app.ActiveUIDocument?.Document;
            // Availability вызывается вне команды: временный IsReadOnly здесь не должен выключать кнопки.
            // Проверка возможности изменения остаётся в TryProject при запуске команды.
            return doc != null && !doc.IsFamilyDocument;
        }
    }

    public static class RevitSupport
    {
        public static bool TryProject(ExternalCommandData data, out UIDocument uidoc, out Document doc)
        {
            uidoc = data.Application.ActiveUIDocument;
            doc = uidoc?.Document;
            if (doc != null && !doc.IsFamilyDocument && !doc.IsReadOnly) return true;
            TaskDialog.Show("ARV", "Откройте проект Revit, доступный для редактирования.");
            return false;
        }

        public static string Text(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return "";
            return parameter.StorageType == StorageType.String ? parameter.AsString() ?? "" : parameter.AsValueString() ?? "";
        }

        public static Parameter Require(Element element, string name, StorageType storage)
        {
            var parameter = element.LookupParameter(name);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != storage)
                throw new InvalidOperationException($"Элемент {element.Id}: параметр «{name}» отсутствует, заблокирован или имеет неверный тип данных.");
            return parameter;
        }

        public static void SetText(Element element, string name, string value)
        {
            var parameter = Require(element, name, StorageType.String);
            if (parameter.AsString() != value && !parameter.Set(value))
                throw new InvalidOperationException($"Не удалось записать «{name}» элемента {element.Id}.");
        }

        public static void SetNumber(Element element, string name, double value)
        {
            var parameter = Require(element, name, StorageType.Double);
            if (Math.Abs(parameter.AsDouble() - value) > 1e-10 && !parameter.Set(value))
                throw new InvalidOperationException($"Не удалось записать «{name}» элемента {element.Id}.");
        }

        public static void Commit(Transaction transaction)
        {
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit не подтвердил изменения. Операция не завершена.");
        }
    }
}
