using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CeilingPlugin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiapp = commandData.Application;
                var doc = uiapp.ActiveUIDocument.Document;

                // 1. Собираем все помещения в проекте
                var rooms = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .Cast<SpatialElement>()
                    .Where(r => r is Room)
                    .Cast<Room>()
                    .ToList();

                // 2. Группируем помещения по значению параметра ВО_СтильПотолок
                var styleValues = new HashSet<string>();
                var roomStyleMap = new Dictionary<string, List<Room>>();

                foreach (var room in rooms)
                {
                    string style = GetParamString(room, "ВО_СтильПотолок");
                    if (string.IsNullOrEmpty(style)) continue;

                    styleValues.Add(style);
                    if (!roomStyleMap.ContainsKey(style))
                        roomStyleMap[style] = new List<Room>();
                    roomStyleMap[style].Add(room);
                }

                if (styleValues.Count == 0)
                {
                    TaskDialog.Show("Внимание", "Не найдено помещений с заполненным параметром 'ВО_СтильПотолок'.");
                    return Result.Cancelled;
                }

                // 3. Собираем доступные типы потолков
                var ceilingTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(CeilingType))
                    .Cast<CeilingType>()
                    .ToList();

                var typeItems = ceilingTypes.Select(ct => new CeilingTypeItem
                {
                    TypeId = ct.Id,
                    TypeName = ct.Name
                }).ToList();
                // Добавляем опцию "нет" в начало списка
                typeItems.Insert(0, new CeilingTypeItem { TypeId = ElementId.InvalidElementId, TypeName = "нет" });

                // 4. Формируем данные для таблицы WPF
                var mappings = styleValues.Select(s => new StyleMapping
                {
                    StyleName = s,
                    SelectedCeilingType = typeItems[0] // по умолчанию выбрано "нет"
                }).ToList();

                // 5. Открываем WPF окно
                var window = new CeilingCreatorWindow(mappings, typeItems);
                if (window.ShowDialog() != true)
                    return Result.Cancelled;

                // 6. Создание потолков в транзакции
                int createdCount = 0;
                using (var trans = new Transaction(doc, "Создание потолков"))
                {
                    trans.Start();
                    foreach (var mapping in mappings)
                    {
                        if (mapping.SelectedCeilingType == null || mapping.SelectedCeilingType.IsNone)
                            continue;

                        if (!roomStyleMap.TryGetValue(mapping.StyleName, out var targetRooms))
                            continue;

                        foreach (var room in targetRooms)
                        {
                            try
                            {
                                if (CreateCeilingForRoom(doc, room, mapping.SelectedCeilingType.TypeId))
                                    createdCount++;
                            }
                            catch (Exception ex)
                            {
                                // Помещения с некорректной геометрией пропускаем без прерывания команды
                                System.Diagnostics.Debug.WriteLine($"Пропущено помещение '{room.Name}' (Id: {room.Id}): {ex.Message}");
                            }
                        }
                    }
                    trans.Commit();
                }

                TaskDialog.Show("Результат", $"Успешно создано потолков: {createdCount}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Безопасное чтение строкового параметра
        /// </summary>
        private string GetParamString(Element el, string paramName)
        {
            var p = el.LookupParameter(paramName);
            return p?.StorageType == StorageType.String ? p.AsString() : null;
        }

        /// <summary>
        /// Создание потолка по контуру помещения
        /// </summary>
        private bool CreateCeilingForRoom(Document doc, Room room, ElementId ceilingTypeId)
        {
            // Получаем границы помещения
            var options = new SpatialElementBoundaryOptions();
            var segments = room.GetBoundarySegments(options);
            if (segments == null || segments.Count == 0) return false;

            // Формируем CurveLoop из первого (внешнего) контура
            var curveLoop = new CurveLoop();
            foreach (var seg in segments[0])
                curveLoop.Append(seg.GetCurve());

            ElementId levelId = room.LevelId;
            if (levelId == ElementId.InvalidElementId) return false;

            // Создаем потолок
            var ceiling = Ceiling.Create(doc, new List<CurveLoop> { curveLoop }, ceilingTypeId, levelId);
            if (ceiling == null) return false;

            // ✅ Устанавливаем, что потолок является границей помещения
            var boundaryParam = ceiling.get_Parameter(BuiltInParameter.ROOM_BOUNDARY);
            if (boundaryParam != null && !boundaryParam.IsReadOnly)
                boundaryParam.Set(1); // 1 = Да

            // Высота потолка = высота помещения (LimitOffset)
            double roomHeight = room.LimitOffset;
            if (roomHeight > 0)
            {
                var offsetParam = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                if (offsetParam != null && !offsetParam.IsReadOnly)
                    offsetParam.Set(roomHeight);
            }

            // Записываем Номер помещения в ADSK_Группирование
            string roomNumber = GetParamString(room, "Номер");
            if (!string.IsNullOrEmpty(roomNumber))
            {
                var adskParam = ceiling.LookupParameter("ADSK_Группирование");
                if (adskParam != null && adskParam.StorageType == StorageType.String && !adskParam.IsReadOnly)
                    adskParam.Set(roomNumber);
            }

            return true;
        }
    }
}