using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FinishingSchedulePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!ARV.Common.RevitSupport.TryProject(commandData, out UIDocument uiDoc, out Document doc)) return Result.Cancelled;

            try
            {
                MainWindow window = new MainWindow(uiDoc);
                window.ShowDialog();

                if (window.IsCancelled)
                {
                    return Result.Cancelled;
                }

                double factor = window.Factor;
                string wallGroupFilter = window.WallGroupFilter;
                string ceilingGroupFilter = window.CeilingGroupFilter;

                string summary;
                using (Transaction tx = new Transaction(doc, "Ведомость чистовой отделки"))
                {
                    tx.Start();
                    summary = ProcessRooms(doc, wallGroupFilter, ceilingGroupFilter, factor);
                    ARV.Common.RevitSupport.Commit(tx);
                }

                TaskDialog.Show("Чистовая отделка", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private string ProcessRooms(Document doc, string wallGroupFilter, string ceilingGroupFilter, double factor)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToList();

            if (rooms.Count == 0)
            {
                throw new InvalidOperationException("В проекте не найдено помещений.");
            }

            var walls = GetFilteredElements(doc, BuiltInCategory.OST_Walls, wallGroupFilter);
            var ceilings = GetFilteredElements(doc, BuiltInCategory.OST_Ceilings, ceilingGroupFilter);

            int processedRoomsCount = 0;
            Dictionary<string, RoomGroupData> roomGroups = new Dictionary<string, RoomGroupData>();

            double coefficient = 1 + (factor / 100);

            foreach (Element roomElem in rooms)
            {
                Room room = roomElem as Room;
                if (room == null)
                    continue;

                Parameter roomNumberParam = room.LookupParameter("Номер") ?? room.LookupParameter("Number");
                if (roomNumberParam == null)
                    continue;

                string roomNumber = ARV.Common.RevitSupport.Text(roomNumberParam);
                if (string.IsNullOrEmpty(roomNumber))
                    continue;

                UpdateSkirtingLength(room, coefficient);

                List<MaterialArea> wallMaterials = GetMaterialAreasFromElements(doc, walls, roomNumber, "ADSK_Группирование", factor);
                List<MaterialArea> ceilingMaterials = GetMaterialAreasFromElements(doc, ceilings, roomNumber, "ADSK_Группирование", factor);

                // 🔧 СОРТИРОВКА по алфавиту перед записью!
                wallMaterials = wallMaterials.OrderBy(m => m.MaterialName).ToList();
                ceilingMaterials = ceilingMaterials.OrderBy(m => m.MaterialName).ToList();

                SetRoomParameterWithAreas(room, "ВО_Стены_Финишная_Материал", "ВО_Стены_Финишная_Площадь", wallMaterials);
                SetRoomParameterWithAreas(room, "ВО_Потолки_Материал", "ВО_Потолки_Площадь", ceilingMaterials);

                Parameter skirtingParam = room.LookupParameter("Тип плинтуса");
                string skirtingValue = skirtingParam != null ? ARV.Common.RevitSupport.Text(skirtingParam) : "";

                // 🔧 Ключ группировки формируется из отсортированных списков
                string ceilingKey = ceilingMaterials.Count > 0 ? string.Join(";", ceilingMaterials.Select(m => m.MaterialName)) : "";
                string wallKey = wallMaterials.Count > 0 ? string.Join(";", wallMaterials.Select(m => m.MaterialName)) : "";
                string groupKey = $"{ceilingKey}|{wallKey}|{skirtingValue}";

                if (!roomGroups.ContainsKey(groupKey))
                {
                    roomGroups[groupKey] = new RoomGroupData();
                }
                roomGroups[groupKey].Rooms.Add(room);
                roomGroups[groupKey].RoomNumbers.Add(roomNumber);

                foreach (var wallMat in wallMaterials)
                {
                    if (!roomGroups[groupKey].WallAreas.ContainsKey(wallMat.MaterialName))
                    {
                        roomGroups[groupKey].WallAreas[wallMat.MaterialName] = 0;
                        roomGroups[groupKey].WallOrder.Add(wallMat.MaterialName);
                    }
                    roomGroups[groupKey].WallAreas[wallMat.MaterialName] += wallMat.Area;
                }

                foreach (var ceilingMat in ceilingMaterials)
                {
                    if (!roomGroups[groupKey].CeilingAreas.ContainsKey(ceilingMat.MaterialName))
                    {
                        roomGroups[groupKey].CeilingAreas[ceilingMat.MaterialName] = 0;
                        roomGroups[groupKey].CeilingOrder.Add(ceilingMat.MaterialName);
                    }
                    roomGroups[groupKey].CeilingAreas[ceilingMat.MaterialName] += ceilingMat.Area;
                }

                processedRoomsCount++;
            }

            foreach (var group in roomGroups)
            {
                group.Value.RoomNumbers.Sort();
                string roomNumbersString = string.Join(", ", group.Value.RoomNumbers);

                foreach (Room room in group.Value.Rooms)
                {
                    ARV.Common.RevitSupport.SetText(room, "ВО_СписокНомеровСтены", string.IsNullOrEmpty(group.Key) ? "" : roomNumbersString);
                    ARV.Common.RevitSupport.SetText(room, "ВО_Стены_Финишная_Площадь", FormatAreaListOrdered(group.Value.WallAreas, group.Value.WallOrder));
                    ARV.Common.RevitSupport.SetText(room, "ВО_Потолки_Площадь", FormatAreaListOrdered(group.Value.CeilingAreas, group.Value.CeilingOrder));
                }
            }

            return $"Обработано помещений: {processedRoomsCount}\n" +
                                         $"Стен найдено: {walls.Count}\n" +
                                         $"Потолков найдено: {ceilings.Count}\n" +
                                         $"Групп помещений: {roomGroups.Count}\n" +
                                         $"Коэффициент запаса: {factor}%";
        }

        private void UpdateSkirtingLength(Room room, double coefficient)
        {
            Parameter skirtingLengthParam = room.LookupParameter("ADSK_Длина плинтуса");
            if (skirtingLengthParam != null && !skirtingLengthParam.IsReadOnly)
            {
                double currentLength = skirtingLengthParam.AsDouble();
                if (currentLength > 0)
                {
                    double newLength = currentLength * coefficient;
                    ARV.Common.RevitSupport.SetNumber(room, "ADSK_Длина плинтуса", newLength);
                }
            }
        }

        private List<Element> GetFilteredElements(Document doc, BuiltInCategory category, string groupFilter)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            var filteredElements = new List<Element>();

            foreach (Element elem in collector)
            {
                if (groupFilter == "Нет")
                    continue;

                ElementId typeId = elem.GetTypeId();
                if (typeId == ElementId.InvalidElementId)
                    continue;

                ElementType elemType = doc.GetElement(typeId) as ElementType;
                if (elemType == null)
                    continue;

                Parameter groupParam = elemType.LookupParameter("Группа модели") ??
                                       elemType.LookupParameter("Группа") ??
                                       elemType.LookupParameter("Model Group");

                if (groupParam != null)
                {
                    string groupValue = ARV.Common.RevitSupport.Text(groupParam);
                    if (groupValue == groupFilter)
                    {
                        filteredElements.Add(elem);
                    }
                }
            }

            return filteredElements;
        }

        private List<MaterialArea> GetMaterialAreasFromElements(Document doc, List<Element> elements, string roomNumber, string groupingParamName, double factor)
        {
            var materials = new List<MaterialArea>();
            var uniqueMaterials = new Dictionary<string, double>();

            double coefficient = 1 + (factor / 100);

            foreach (Element elem in elements)
            {
                Parameter groupingParam = elem.LookupParameter(groupingParamName);
                if (groupingParam == null)
                    continue;

                string elemGroupNumber = ARV.Common.RevitSupport.Text(groupingParam);
                if (elemGroupNumber != roomNumber)
                    continue;

                ElementId typeId = elem.GetTypeId();
                if (typeId == ElementId.InvalidElementId)
                    continue;

                ElementType elemType = doc.GetElement(typeId) as ElementType;
                if (elemType == null)
                    continue;

                Parameter descParam = elemType.LookupParameter("ВО_Описание");
                if (descParam != null)
                {
                    string materialValue = ARV.Common.RevitSupport.Text(descParam);
                    if (!string.IsNullOrEmpty(materialValue))
                    {
                        Parameter areaParam = elem.LookupParameter("Площадь") ?? elem.LookupParameter("Area");
                        double area = 0;
                        if (areaParam != null)
                        {
                            area = areaParam.AsDouble();
                        }

                        area = area * coefficient;

                        if (!uniqueMaterials.ContainsKey(materialValue))
                        {
                            uniqueMaterials[materialValue] = 0;
                        }
                        uniqueMaterials[materialValue] += area;
                    }
                }
            }

            foreach (var kvp in uniqueMaterials)
            {
                materials.Add(new MaterialArea { MaterialName = kvp.Key, Area = kvp.Value });
            }

            return materials;
        }

        private void SetRoomParameterWithAreas(Room room, string materialParamName, string areaParamName, List<MaterialArea> materials)
        {
            string materialValue = "";
            string areaValue = "";

            for (int i = 0; i < materials.Count; i++)
            {
                if (i > 0)
                {
                    materialValue += "\r\n";
                    areaValue += "\r\n";
                }
                materialValue += $"{i + 1}. {materials[i].MaterialName}";

                double areaInM2 = UnitUtils.ConvertFromInternalUnits(materials[i].Area, UnitTypeId.SquareMeters);
                areaValue += $"{i + 1}. {areaInM2:F2}";
            }

            ARV.Common.RevitSupport.SetText(room, materialParamName, materialValue);
            ARV.Common.RevitSupport.SetText(room, areaParamName, areaValue);
        }

        private string FormatAreaListOrdered(Dictionary<string, double> areas, List<string> order)
        {
            if (areas.Count == 0)
                return "";

            string result = "";
            int index = 1;
            foreach (var materialName in order)
            {
                if (areas.ContainsKey(materialName))
                {
                    if (index > 1)
                        result += "\r\n";
                    double areaInM2 = UnitUtils.ConvertFromInternalUnits(areas[materialName], UnitTypeId.SquareMeters);
                    result += $"{index}. {areaInM2:F2}";
                    index++;
                }
            }
            return result;
        }
    }

    public class RoomGroupData
    {
        public List<Room> Rooms { get; set; } = new List<Room>();
        public List<string> RoomNumbers { get; set; } = new List<string>();
        public Dictionary<string, double> WallAreas { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> CeilingAreas { get; set; } = new Dictionary<string, double>();
        public List<string> WallOrder { get; set; } = new List<string>();
        public List<string> CeilingOrder { get; set; } = new List<string>();
    }

    public class MaterialArea
    {
        public string MaterialName { get; set; }
        public double Area { get; set; }
    }
}