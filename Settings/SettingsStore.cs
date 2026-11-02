using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TakeABreakGoose;

/// <summary>Reads and writes the settings file in %APPDATA%\TakeABreakGoose.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TakeABreakGoose",
        "settings.json");

    public static GooseSettings Load()
    {
        GooseSettings settings;
        try
        {
            settings = File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new GooseSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file shouldn't keep the goose from showing up.
            settings = new GooseSettings();
        }
        settings.Sanitize();
        return settings;
    }

    public static void Save(GooseSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Serialize(settings));
    }

    /// <summary>The settings as the file's JSON.</summary>
    public static string Serialize(GooseSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    /// <summary>Settings from the file's JSON, before sanitizing.</summary>
    public static GooseSettings Parse(string json) =>
        JsonSerializer.Deserialize<GooseSettings>(json, JsonOptions) ?? new GooseSettings();
}
