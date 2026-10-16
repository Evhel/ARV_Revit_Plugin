using System;
using System.Collections.Generic;
using System.Linq;

namespace ARV.AreaReinforcementHosts
{
    public static class HostMatchingMath
    {
        public static IList<int> LowerPointIndices(IList<double> heights, double tolerance)
        {
            if (heights == null || heights.Count == 0) throw new ArgumentException("Нет точек контура.");
            if (heights.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new ArgumentException("Некорректный контур.");
            if (tolerance < 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
            double bottom = heights.Min();
            return Enumerable.Range(0, heights.Count).Where(i => heights[i] <= bottom + tolerance).ToList();
        }

        public static bool WithinSegment(double distanceToStart, double distanceToEnd, double segmentLength, double tolerance)
        {
            return distanceToStart + distanceToEnd <= segmentLength + 2 * tolerance;
        }

        public static int UniqueHost(IEnumerable<int> matches)
        {
            var ids = matches.Distinct().Take(2).ToList();
            if (ids.Count == 0) throw new InvalidOperationException("Нижняя граница не найдена внутри подходящей стены выбранного этажа.");
            if (ids.Count > 1) throw new InvalidOperationException("Под нижней границей найдены несколько стен. Выберите основу одиночной командой.");
            return ids[0];
        }
    }
}