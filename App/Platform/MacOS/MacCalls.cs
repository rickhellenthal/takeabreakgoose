using System;
using System.Linq;

namespace TakeABreakGoose;

/// <summary>
/// Tells whether the user is in a call: an app is using the camera, or a meeting app is using the
/// microphone (from macOS 14.2; before that only the camera counts).
/// </summary>
/// <remarks>
/// The microphone only counts for meeting apps: games, Discord and Steam use it for voice chat,
/// where the goose is still welcome. An app missing from the list means the goose shows up as
/// usual, which is better than it quietly staying away for hours.
/// </remarks>
public static class MacCalls
{
    // By bundle ID. Browsers count: Google Meet and Teams run in them. Their helper processes (where
    // Chrome and Edge record audio) have IDs that start with the browser's, so those count too.
    private static readonly string[] MeetingApps =
    {
        "us.zoom.xos", "com.microsoft.teams2", "com.microsoft.teams", "com.cisco.webexmeetingsapp",
        "Cisco-Systems.Spark", "com.tinyspeck.slackmacgap", "com.skype.skype", "com.logmein.GoToMeeting",
        "com.apple.FaceTime",
        "com.google.Chrome", "com.microsoft.edgemac", "org.mozilla.firefox", "com.brave.Browser",
        "com.operasoftware.Opera", "com.vivaldi.Vivaldi", "company.thebrowser.Browser",
        // Safari records in WebKit's shared media process.
        "com.apple.Safari", "com.apple.WebKit.GPU",
    };

    public static bool InProgress() => MacCameraActivity.InUse() || MacMicrophoneActivity.Apps().Any(IsMeetingApp);

    /// <summary>Whether people hold meetings in this app, or it's one of that app's helpers.</summary>
    internal static bool IsMeetingApp(string bundleId) =>
        MeetingApps.Any(app => bundleId.Equals(app, StringComparison.OrdinalIgnoreCase)
                               || bundleId.StartsWith(app + ".", StringComparison.OrdinalIgnoreCase));
}
