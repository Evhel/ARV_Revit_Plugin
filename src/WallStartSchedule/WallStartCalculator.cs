using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using ARV.Common;

namespace WallStartSchedule
{
    public class WallStartCalculator
    {
        private readonly Document doc;
        public WallStartCalculator(Document document) { doc = document; }
        private static string MaterialParameter(int column) => $"ВО_Стены_Черновая_{column}_Материал";
        private static string AreaParameter(int column) => $"ВО_Стены_Черновая_{column}_Площадь";
        private const string ListParameter = "ВО_СписокНомеровСтены_Черновая";

        public CalculateResult CalculateAndWrite(Dictionary<int, ElementId> selectedTypes, double areaCoefficient)
        {
            try
            {
                if (selectedTypes.Count == 0 || selectedTypes.Keys.Any(k => k < 1 || k > 5)) throw new InvalidOperationException("Выберите типы отделки для колонок 1–5.");
                if (!Algorithms.TryNumber(areaCoefficient.ToString(System.Globalization.CultureInfo.InvariantCulture), out double coefficient) || coefficient < 0 || coefficient > 100)
                    throw new InvalidOperationException("Коэффициент должен быть от 0 до 100.");
                if (selectedTypes.Values.Select(id => id.IntegerValue).Distinct().Count() != selectedTypes.Count)
                    throw new InvalidOperationException("Один тип стены нельзя выбрать для нескольких колонок.");
                var rooms = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Room>()
                    .Where(r => r.Area > 0 && !string.IsNullOrWhiteSpace(r.Number)).ToList();
                if (rooms.Count == 0) throw new InvalidOperationException("Нет размещённых помещений с площадью и номером.");
                var duplicates = rooms.GroupBy(r => r.Number).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (duplicates.Count > 0) throw new InvalidOperationException("Неоднозначные номера помещений: " + string.Join(", ", duplicates) + ". ADSK_Группирование не позволяет однозначно сопоставить стены.");
                foreach (var room in rooms)
                {
                    RevitSupport.Require(room, ListParameter, StorageType.String);
                    for (int column = 1; column <= 5; column++)
                    {
                        RevitSupport.Require(room, MaterialParameter(column), StorageType.String);
                        RevitSupport.Require(room, AreaParameter(column), StorageType.Double);
                    }
                }
                var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
                var selectedIds = new HashSet<int>(selectedTypes.Values.Select(id => id.IntegerValue));
                var filtered = walls.Where(w => selectedIds.Contains(w.GetTypeId().IntegerValue)).ToList();
                var byRoom = filtered.ToLookup(w => RevitSupport.Text(w.LookupParameter("ADSK_Группирование")));
                var groupRooms = new Dictionary<string, List<Room>>();
                using (var transaction = new Transaction(doc, "Черновая отделка стен"))
                {
                    transaction.Start();
                    foreach (var room in rooms)
                    {
                        for (int column = 1; column <= 5; column++)
                        {
                            RevitSupport.SetText(room, MaterialParameter(column), "");
                            RevitSupport.SetNumber(room, AreaParameter(column), 0);
                        }
                        var present = new List<int>();
                        foreach (var selected in selectedTypes.OrderBy(p => p.Key))
                        {
                            var matches = byRoom[room.Number].Where(w => w.GetTypeId().IntegerValue == selected.Value.IntegerValue).ToList();
                            if (matches.Count == 0) continue;
                            present.Add(selected.Key);
                            var type = doc.GetElement(selected.Value);
                            string description = RevitSupport.Text(type.LookupParameter("ВО_Описание"));
                            if (string.IsNullOrWhiteSpace(description)) throw new InvalidOperationException("У типа «" + type.Name + "» не заполнен ВО_Описание.");
                            double area = 0;
                            foreach (var wall in matches)
                            {
                                var parameter = wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                                if (parameter == null || parameter.StorageType != StorageType.Double) throw new InvalidOperationException("Не найдена площадь стены " + wall.Id);
                                area += parameter.AsDouble();
                            }
                            RevitSupport.SetText(room, MaterialParameter(selected.Key), description);
                            RevitSupport.SetNumber(room, AreaParameter(selected.Key), area * (1 + coefficient / 100.0));
                        }
                        string key = string.Join(",", present);
                        if (!groupRooms.ContainsKey(key)) groupRooms[key] = new List<Room>();
                        groupRooms[key].Add(room);
                    }
                    foreach (var group in groupRooms)
                    {
                        string numbers = group.Key.Length == 0 ? "" : string.Join(", ", group.Value.Select(r => r.Number).OrderBy(n => n, Algorithms.NameComparer));
                        foreach (var room in group.Value) RevitSupport.SetText(room, ListParameter, numbers);
                    }
                    RevitSupport.Commit(transaction);
                }
                return new CalculateResult { Success = true, RoomsCount = rooms.Count, TotalWallsCount = walls.Count, FilteredWallsCount = filtered.Count };
            }
            catch (Exception ex) { return new CalculateResult { Success = false, ErrorMessage = ex.Message }; }
        }
    }
    public class CalculateResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int RoomsCount { get; set; }
        public int TotalWallsCount { get; set; }
        public int FilteredWallsCount { get; set; }
    }
}
