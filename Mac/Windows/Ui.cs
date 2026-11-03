using System;

namespace TakeABreakGoose;

/// <summary>Small helpers shared by the pages of the app window.</summary>
internal static class Ui
{
    /// <summary>Reads a whole number in a range from a text box, or reports what's wrong with it.</summary>
    public static bool TryParseNumber(string? text, int min, int max, string name, out int value, Action<string> fail)
    {
        if (int.TryParse((text ?? "").Trim(), out value) && value >= min && value <= max) return true;
        fail(string.Format(Strings.WholeNumberBetween, name, min, max));
        return false;
    }
}
