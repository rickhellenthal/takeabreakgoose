using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TakeABreakGoose;

/// <summary>
/// A running focus session, written down just before the app restarts for an update, so the new
/// version carries on with it: the same session, the same next break, the breaks so far. Updates
/// never happen during a break (see UpdateMoment), so that's all there is to it.
/// </summary>
public sealed record SessionSnapshot(
    WorkSession Session, DateTime? NextBreakAt, bool DoNotDisturb, IReadOnlyList<BreakRecord> Breaks, DateTime SavedAt)
{
    /// <summary>Only picked up shortly after: an update that took longer means the user moved on.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public bool IsFresh(DateTime now) => now >= SavedAt && now - SavedAt <= KeptFor;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Null when the text isn't a snapshot.</summary>
    public static SessionSnapshot? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionSnapshot>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
