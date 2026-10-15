using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARV.Common
{
    public static class Algorithms
    {
        public static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true);

        public static bool TryNumber(string text, out double value)
        {
            return double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static string UniqueName(string requested, ISet<string> used)
        {
            string result = requested;
            for (int suffix = 2; used.Contains(result); suffix++) result = requested + "_" + suffix;
            used.Add(result);
            return result;
        }

        public static string LetterNumber(int index, string alphabet)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            long value = (long)index + 1;
            string result = "";
            while (value > 0)
            {
                value--;
                result = alphabet[(int)(value % alphabet.Length)] + result;
                value /= alphabet.Length;
            }
            return result;
        }

    }
}
