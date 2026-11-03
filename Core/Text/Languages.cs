using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TakeABreakGoose;

/// <summary>A language the app speaks: its code in the settings, and its name in that language.</summary>
public sealed record Language(string Code, string Name);

/// <summary>
/// The languages the app speaks, and which one it speaks right now. The texts themselves are in
/// Strings.resx (English) and Strings.nl.resx (Dutch), and come from the generated <see cref="Strings"/> class.
/// </summary>
public static class Languages
{
    public const string English = "en";
    public const string Dutch = "nl";

    /// <summary>Each with its own name, so it can be found whichever language the app speaks now.</summary>
    public static readonly IReadOnlyList<Language> All = new[]
    {
        new Language(English, "English"),
        new Language(Dutch, "Nederlands"),
    };

    public static bool IsKnown(string? code) => All.Any(l => l.Code == code);

    /// <summary>The language the texts are in right now.</summary>
    public static string Current => Strings.Culture.Name;

    /// <summary>Switches every text the app shows from now on to this language (English if it's not known).</summary>
    public static void Use(string code) => Strings.Culture = Culture(IsKnown(code) ? code : English);

    public static CultureInfo Culture(string code) => CultureInfo.GetCultureInfo(code);

    /// <summary>
    /// English until the app says otherwise. Without a language set, the texts would follow the
    /// computer's own language, also before the settings are loaded, and in the tests.
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255", Justification = "Every user of the core, the apps and the tests alike, needs this before the first text.")]
    internal static void UseEnglishByDefault() => Use(English);

    /// <summary>A text from the resources in a language other than the current one.</summary>
    internal static string Text(string name, string code) => Strings.ResourceManager.GetString(name, Culture(code)) ?? "";

    /// <summary>A text that holds a list, one item per line.</summary>
    public static string[] Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
}
