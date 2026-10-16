using System;
using System.Linq;
using ARV.AreaReinforcementHosts;

internal static class AreaHostMathTests
{
    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; }
    }
    internal static void Run(Action<bool, string> check)
    {
        var heights = new[] { 3.0, 9.0, 9.0, 3.0 };
        var lower = HostMatchingMath.LowerPointIndices(heights, 0.002);
        check(lower.SequenceEqual(new[] { 0, 3 }), "only lower contour, upper may exceed wall");
        check(lower.All(i => heights[i] >= 3 && heights[i] <= 6), "contour higher than wall is accepted by lower boundary");
        check(!lower.All(i => heights[i] >= 6 && heights[i] <= 9), "upper-storey wall rejected by lower boundary");
        check(HostMatchingMath.LowerPointIndices(new[] { 3.001, 3.0, 9.0 }, 0.002).Count == 2, "lower tolerance");
        check(HostMatchingMath.LowerPointIndices(new[] { -3.0, 0.0, -3.0 }, 0.001).SequenceEqual(new[] { 0, 2 }), "below-ground floor");
        check(HostMatchingMath.UniqueHost(new[] { 42 }) == 42, "one host");
        check(HostMatchingMath.UniqueHost(new[] { 42, 42 }) == 42, "duplicate host evidence");
        check(Throws(() => HostMatchingMath.UniqueHost(new int[0])), "missing host never guessed");
        check(Throws(() => HostMatchingMath.UniqueHost(new[] { 42, 43 })), "ambiguous host never guessed");
        check(HostMatchingMath.WithinSegment(0.4, 0.6, 1.0, 0.001), "point within solid interval");
        check(!HostMatchingMath.WithinSegment(4, 5, 1, 0.001), "projection onto infinite normal does not imply inside wall");
        check(HostMatchingMath.WithinSegment(0.001, 1.001, 1, 0.002), "solid boundary tolerance");
        check(Throws(() => HostMatchingMath.LowerPointIndices(new double[0], 0.001)), "empty contour rejected");
        check(Throws(() => HostMatchingMath.LowerPointIndices(new[] { double.NaN, 0.0 }, 0.001)), "NaN geometry rejected");
        check(Throws(() => HostMatchingMath.LowerPointIndices(new[] { 0.0 }, -1)), "negative tolerance rejected");
    }
}