using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WallStartSchedule
{
    public class WallStartCalculator
    {
        private Document _doc;

        private readonly string[] _roomMaterialParamNames = new string[]
        {
            "ВО_Стены_Черновая_1_Материал",
            "ВО_Стены_Черновая_2_Материал",
            "ВО_Стены_Черновая_3_Материал",
            "ВО_Стены_Черновая_4_Материал",
            "ВО_Стены_Черновая_5_Материал"
        };

        private readonly string[] _roomAreaParamNames = new string[]
        {
            "ВО_Стены_Черновая_1_Площадь",
            "ВО_Стены_Черновая_2_Площадь",
            "ВО_Стены_Черновая_3_Площадь",
            "ВО_Стены_Черновая_4_Площадь",
            "ВО_Стены_Черновая_5_Площадь"
        };

        private readonly string _roomListParamName = "ВО_СписокНомеровСтены_Черновая";

        public WallStartCalculator(Document doc)
        {
            _doc = doc;
        }

        public CalculateResult CalculateAndWrite(Dictionary<int, string> selectedTypes, Dictionary<int, string> columnNames, double areaCoefficient)
        {
            try
            {
                using (Transaction trans = new Transaction(_doc, "Запись черновой отделки стен"))
                {
                    trans.Start();

                    FilteredElementCollector roomCollector = new FilteredElementCollector(_doc);
                    roomCollector.OfCategory(BuiltInCategory.OST_Rooms);
                    var rooms = roomCollector.WhereElementIsNotElementType().ToList();
                    int roomsCount = rooms.Count;

                    if (roomsCount == 0)
                    {
                        trans.RollBack();
                        return new CalculateResult { Success = false, ErrorMessage = "В проекте не найдено помещений" };
                    }

                    FilteredElementCollector wallCollector = new FilteredElementCollector(_doc);
                    wallCollector.OfCategory(BuiltInCategory.OST_Walls);
                    wallCollector.WhereElementIsNotElementType();
                    var allWalls = wallCollector.ToList();
                    int totalWallsCount = allWalls.Count;

                    var selectedTypeNames = selectedTypes.Values.ToHashSet();
                    var filteredWalls = allWalls.Where(w =>
                    {
                        ElementType wallType = _doc.GetElement(w.GetTypeId()) as ElementType;
                        return wallType != null && selectedTypeNames.Contains(wallType.Name);
                    }).ToList();
                    int filteredWallsCount = filteredWalls.Count;

                    // Вычисляем множитель из коэффициента (0% = 1.0, 100% = 2.0)
                    double areaMultiplier = 1.0 + (areaCoefficient / 100.0);

                    Dictionary<string, List<string>> roomGroups = new Dictionary<string, List<string>>();

                    foreach (var room in rooms)
                    {
                        string roomNumber = GetParameterValue(room, "Номер");

                        if (string.IsNullOrEmpty(roomNumber))
                            continue;

                        for (int i = 0; i < _roomMaterialParamNames.Length; i++)
                        {
                            SetParameterValue(room, _roomMaterialParamNames[i], "");
                            SetParameterValue(room, _roomAreaParamNames[i], 0);
                        }
                        SetParameterValue(room, _roomListParamName, "");

                        var roomWalls = filteredWalls.Where(w =>
                        {
                            string wallGroup = GetParameterValue(w, "ADSK_Группирование");
                            return wallGroup == roomNumber;
                        }).ToList();

                        List<int> presentColumns = new List<int>();

                        foreach (var kvp in selectedTypes)
                        {
                            int columnIndex = kvp.Key;
                            string wallTypeName = kvp.Value;

                            if (columnIndex < 1 || columnIndex > 5)
                                continue;

                            var matchingWalls = roomWalls.Where(w =>
                            {
                                ElementType wallType = _doc.GetElement(w.GetTypeId()) as ElementType;
                                return wallType != null && wallType.Name == wallTypeName;
                            }).ToList();

                            if (matchingWalls.Count > 0)
                            {
                                presentColumns.Add(columnIndex);

                                ElementType wallType = _doc.GetElement(matchingWalls[0].GetTypeId()) as ElementType;

                                if (wallType != null)
                                {
                                    string description = GetElementTypeParameterValue(wallType, "ВО_Описание");

                                    string roomMaterialParamName = _roomMaterialParamNames[columnIndex - 1];
                                    SetParameterValue(room, roomMaterialParamName, description ?? "");

                                    double totalArea = 0;
                                    foreach (Wall wall in matchingWalls)
                                    {
                                        totalArea += CalculateWallArea(wall);
                                    }

                                    // Применяем коэффициент запаса
                                    totalArea = totalArea * areaMultiplier;

                                    string roomAreaParamName = _roomAreaParamNames[columnIndex - 1];
                                    SetParameterValue(room, roomAreaParamName, totalArea);
                                }
                            }
                        }

                        string groupKey = presentColumns.Count > 0
                            ? string.Join(",", presentColumns.OrderBy(c => c))
                            : "EMPTY";

                        if (!roomGroups.ContainsKey(groupKey))
                        {
                            roomGroups[groupKey] = new List<string>();
                        }
                        roomGroups[groupKey].Add(roomNumber);
                    }

                    foreach (var group in roomGroups)
                    {
                        string groupKey = group.Key;
                        List<string> roomNumbers = group.Value;

                        string roomListString = string.Join(", ", roomNumbers.OrderBy(n => n));

                        foreach (var room in rooms)
                        {
                            string roomNumber = GetParameterValue(room, "Номер");

                            if (string.IsNullOrEmpty(roomNumber))
                                continue;

                            var roomWalls = filteredWalls.Where(w =>
                            {
                                string wallGroup = GetParameterValue(w, "ADSK_Группирование");
                                return wallGroup == roomNumber;
                            }).ToList();

                            List<int> presentColumns = new List<int>();

                            foreach (var kvp in selectedTypes)
                            {
                                int columnIndex = kvp.Key;
                                string wallTypeName = kvp.Value;

                                if (columnIndex < 1 || columnIndex > 5)
                                    continue;

                                var matchingWalls = roomWalls.Where(w =>
                                {
                                    ElementType wallType = _doc.GetElement(w.GetTypeId()) as ElementType;
                                    return wallType != null && wallType.Name == wallTypeName;
                                }).ToList();

                                if (matchingWalls.Count > 0)
                                {
                                    presentColumns.Add(columnIndex);
                                }
                            }

                            string roomGroupKey = presentColumns.Count > 0
                                ? string.Join(",", presentColumns.OrderBy(c => c))
                                : "EMPTY";

                            if (roomGroupKey == groupKey)
                            {
                                if (groupKey == "EMPTY")
                                {
                                    SetParameterValue(room, _roomListParamName, "");
                                }
                                else
                                {
                                    SetParameterValue(room, _roomListParamName, roomListString);
                                }
                            }
                        }
                    }

                    trans.Commit();

                    return new CalculateResult
                    {
                        Success = true,
                        RoomsCount = roomsCount,
                        TotalWallsCount = totalWallsCount,
                        FilteredWallsCount = filteredWallsCount
                    };
                }
            }
            catch (Exception ex)
            {
                return new CalculateResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private double CalculateWallArea(Wall wall)
        {
            try
            {
                Parameter areaParam = wall.LookupParameter("Площадь");

                if (areaParam != null && areaParam.HasValue)
                {
                    return areaParam.AsDouble();
                }

                areaParam = wall.LookupParameter("Area");

                if (areaParam != null && areaParam.HasValue)
                {
                    return areaParam.AsDouble();
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private string GetParameterValue(Element element, string paramName)
        {
            try
            {
                Parameter param = element.LookupParameter(paramName);
                if (param != null && param.HasValue)
                {
                    return param.AsString();
                }
            }
            catch { }
            return "";
        }

        private void SetParameterValue(Element element, string paramName, string value)
        {
            try
            {
                Parameter param = element.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(value);
                }
            }
            catch { }
        }

        private void SetParameterValue(Element element, string paramName, double value)
        {
            try
            {
                Parameter param = element.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(value);
                }
            }
            catch { }
        }

        private string GetElementTypeParameterValue(ElementType elementType, string paramName)
        {
            try
            {
                Parameter param = elementType.LookupParameter(paramName);
                if (param != null && param.HasValue)
                {
                    return param.AsString();
                }
            }
            catch { }
            return "";
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