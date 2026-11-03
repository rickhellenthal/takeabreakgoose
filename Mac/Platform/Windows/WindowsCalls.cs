using System.Linq;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>
/// Tells whether the user is in a call: an app is using the camera, or a meeting app is using the
/// microphone.
/// </summary>
/// <remarks>
/// The microphone only counts for meeting apps: games, Discord and Steam use it for voice chat,
/// where the goose is still welcome. An app missing from the list means the goose shows up as
/// usual, which is better than it quietly staying away for hours.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsCalls
{
    // By program name. Browsers count: Google Meet and Teams run in them.
    private static readonly string[] MeetingPrograms =
    {
        "ms-teams", "teams", "zoom", "cpthost", "webex", "ciscocollabhost", "atmgr", "slack", "skype",
        "gotomeeting", "g2mcomm", "goto",
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc",
    };

    public static bool InProgress() =>
        WindowsCameraActivity.InUse || WindowsMicrophoneActivity.Programs().Any(IsMeetingApp);

    /// <summary>Whether people hold meetings in this program (by its name, like "zoom").</summary>
    internal static bool IsMeetingApp(string program) => MeetingPrograms.Contains(program.ToLowerInvariant());
}
