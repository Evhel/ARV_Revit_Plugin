using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.ExtensibleStorage;
using ARV.Common;

namespace ARV.AreaReinforcementHosts
{
    public sealed class RehostResult
    {
        public string OldId { get; set; }
        public string NewId { get; set; }
        public string HostId { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
    }

    internal static class RehostService
    {
        internal static List<RehostResult> Run(Document doc, IList<ElementId> ids, Element explicitHost, IList<Wall> floorWalls)
        {
            var results = new List<RehostResult>();
            using (var transaction = new Transaction(doc, "ARV — смена основы армирования по площади"))
            {
                transaction.Start();
                var options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new OutsideHostWarnings());
                transaction.SetFailureHandlingOptions(options);
                foreach (var id in ids)
                {
                    var result = new RehostResult { OldId = id.IntegerValue.ToString(), NewId = "—", HostId = "—", Status = "Пропущено" };
                    using (var item = new SubTransaction(doc))
                    {
                        item.Start();
                        try
                        {
                            var source = doc.GetElement(id) as AreaReinforcement;
                            if (source == null) throw new InvalidOperationException("Армирование по площади больше не существует.");
                            var snapshot = new AreaSnapshot(doc, source);
                            var host = explicitHost ?? HostGeometry.FindWall(doc, snapshot, floorWalls);
                            result.HostId = host.Id.IntegerValue.ToString();
                            if (source.GetHostId() == host.Id)
                            {
                                item.RollBack();
                                result.Status = "Пропущено";
                                result.Detail = "Уже имеет найденную основу.";
                                results.Add(result);
                                continue;
                            }
                            ElementId oldHost = source.GetHostId();
                            var otherAreas = source.GetDependentElements(new ElementClassFilter(typeof(AreaReinforcement)))
                                .Where(dependent => dependent != source.Id).ToList();
                            if (otherAreas.Count > 0) throw new InvalidOperationException("Есть зависимые зоны армирования; автоматическое удаление не выполняется.");
                            var target = AreaReinforcement.Create(doc, host, snapshot.Boundary.Select(e => e.Curve).ToList(),
                                snapshot.Direction, snapshot.TypeId, snapshot.BarType, ElementId.InvalidElementId);
                            if (target == null || target.GetHostId() != host.Id) throw new InvalidOperationException("Revit не назначил выбранную основу.");
                            snapshot.Apply(doc, target);
                            doc.Regenerate();
                            snapshot.Verify(target);
                            foreach (var guid in source.GetEntitySchemaGuids())
                            {
                                var schema = Schema.Lookup(guid);
                                if (schema == null) throw new InvalidOperationException("Не найдена схема данных исходного армирования.");
                                var entity = source.GetEntity(schema);
                                if (!entity.IsValid()) throw new InvalidOperationException("Не удалось прочитать дополнительные данные армирования.");
                                target.SetEntity(entity);
                            }
                            var option = target.DesignOption == null ? ElementId.InvalidElementId : target.DesignOption.Id;
                            if (option != snapshot.OptionId) throw new InvalidOperationException("Нельзя сохранить вариант конструкции в текущем режиме редактирования.");
                            if (source.Pinned) source.Pinned = false;
                            ElementId newId = target.Id;
                            doc.Delete(source.Id); // Также могут удалиться старые марки и размеры — согласовано с владельцем.
                            doc.Regenerate();
                            if (doc.GetElement(oldHost) == null || doc.GetElement(host.Id) == null || doc.GetElement(newId) == null)
                                throw new InvalidOperationException("Удаление затронуло основу или новое армирование; выполнен откат.");
                            target.Pinned = snapshot.Pinned;
                            snapshot.Verify(target);
                            if (target.GetHostId() != host.Id) throw new InvalidOperationException("После пересоздания изменилась основа.");
                            if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Изменение не подтверждено Revit.");
                            result.NewId = newId.IntegerValue.ToString();
                            result.Status = "Изменено";
                            result.Detail = "Контур и настройки сохранены. Старые марки/размеры могут потерять связь.";
                        }
                        catch (Exception ex)
                        {
                            if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                            result.Detail = ex.Message;
                        }
                    }
                    results.Add(result);
                }
                if (results.Any(r => r.Status == "Изменено")) RevitSupport.Commit(transaction);
                else transaction.RollBack();
            }
            return results;
        }
    }
}