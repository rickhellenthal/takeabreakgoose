using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TakeABreakGoose;

/// <summary>A heading of the release notes, with its points in a few words each.</summary>
public sealed record ReleaseNotesSection(string Title, IReadOnlyList<string> Points);

/// <summary>
/// Turns a release's notes (release-notes/vX.Y.Z.md, which Velopack packs with the update) into a
/// short list for the "what's new" window: each heading with the bold start of each of its points.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>The sections up to the Install section, which is only for the release page.</summary>
    public static IReadOnlyList<ReleaseNotesSection> Highlights(string? markdown)
    {
        var sections = new List<ReleaseNotesSection>();
        if (string.IsNullOrWhiteSpace(markdown)) return sections;

        string? title = null;
        var points = new List<string>();
        string? firstSentence = null;

        void Finish()
        {
            if (title == null) return;
            // A section without points: its first sentence says what it's about.
            if (points.Count == 0 && firstSentence != null) points.Add(firstSentence);
            sections.Add(new ReleaseNotesSection(title, points.ToList()));
        }

        foreach (string rawLine in markdown.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (line.StartsWith('#'))
            {
                Finish();
                string heading = PlainText(line.TrimStart('#'));
                if (heading.Equals("Install", StringComparison.OrdinalIgnoreCase)) return sections;
                title = heading;
                points = new List<string>();
                firstSentence = null;
            }
            else if (title == null)
            {
                // Text before the first heading has nowhere to go.
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                points.Add(Point(line[2..]));
            }
            else if (firstSentence == null && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("```"))
            {
                firstSentence = FirstSentence(PlainText(line));
            }
        }
        Finish();
        return sections;
    }

    /// <summary>"**A smaller download**: about a third smaller" → "A smaller download".</summary>
    private static string Point(string text)
    {
        var bold = LeadingBold().Match(text);
        return bold.Success ? PlainText(bold.Groups[1].Value).TrimEnd(':') : PlainText(text);
    }

    private static string FirstSentence(string text)
    {
        int end = text.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + 1)];
    }

    /// <summary>Without the Markdown: bold, italics, code and links become their text.</summary>
    private static string PlainText(string markdown)
    {
        string text = Link().Replace(markdown, "$1");
        text = text.Replace("**", "").Replace("`", "");
        text = Italics().Replace(text, "$1");
        return text.Trim();
    }

    [GeneratedRegex(@"^\*\*(.+?)\*\*")]
    private static partial Regex LeadingBold();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<!\w)[*_]([^*_]+)[*_](?!\w)")]
    private static partial Regex Italics();
}
