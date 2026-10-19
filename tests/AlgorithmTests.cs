using System;
using System.Collections.Generic;
using System.Linq;
using ARV.Common;

public static class AlgorithmTests
{
    private static int count;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    public static int Main()
    {
        double number;
        Check(Algorithms.TryNumber("12,5", out number) && number == 12.5, "decimal comma");
        Check(Algorithms.TryNumber("12.5", out number) && number == 12.5, "decimal dot");
        Check(Algorithms.TryNumber(" -2,5 ", out number) && number == -2.5, "negative crop offset");
        foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity", "1e500", "", "abc", "1,2,3" })
            Check(!Algorithms.TryNumber(invalid, out number), "reject " + invalid);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "РН_АР", "РН_АР_2" };
        Check(Algorithms.UniqueName("РН_АР", names) == "РН_АР_3", "repeat workset command");
        Check(Algorithms.UniqueName("рн_ар", names) == "рн_ар_4", "case insensitive names");
        Check(Algorithms.LetterNumber(0, "АБВ") == "А", "first letter");
        Check(Algorithms.LetterNumber(2, "АБВ") == "В", "last letter");
        Check(Algorithms.LetterNumber(3, "АБВ") == "АА", "letter continuation");
        Check(Algorithms.LetterNumber(11, "АБВ") == "ВВ", "last pair");
        Check(Algorithms.LetterNumber(12, "АБВ") == "ААА", "third letter");
        Check(Algorithms.LetterNumber(int.MaxValue, "АБВ").Length > 0, "overflow-safe lettering");
        Check(new[] { "Я_разрез", "б_разрез", "А_разрез" }.OrderBy(s => s, Algorithms.NameComparer).SequenceEqual(new[] { "А_разрез", "б_разрез", "Я_разрез" }), "Russian name order");
        Check(Algorithms.NameComparer.Compare("Вид", "вид") == 0, "case insensitive view order");
        AreaHostMathTests.Run(Check);
        ElementViewTests.Run(Check);
        ScheduleTests.Run(Check);
        ScheduleFilterRegression.Run(Check);
        SheetFormatRegression.Run(Check);
        Console.WriteLine("PASS: " + count + " algorithm regression checks");
        return 0;
    }
}
