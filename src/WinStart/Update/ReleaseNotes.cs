using System.Text.RegularExpressions;

namespace WinStart.Update;

/// <summary>Turns the Markdown of a GitHub release description into readable plain text.</summary>
internal static class ReleaseNotes
{
    private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant);
    private static readonly Regex Emphasis = new Regex(@"(\*\*|__|`)", RegexOptions.CultureInvariant);
    private static readonly Regex NumberedItem = new Regex(@"^\d+\.\s", RegexOptions.CultureInvariant);

    public static string ToPlainText(string markdown)
    {
        var lines = new List<string>();
        bool previousWasHeading = false;

        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string text = raw.Trim();
            if (text.Length == 0)
            {
                lines.Add("");
                previousWasHeading = false;
                continue;
            }

            bool heading = text[0] == '#';
            bool bullet = text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal);
            bool startsBlock = heading || bullet || NumberedItem.IsMatch(text);

            if (heading) text = text.TrimStart('#').Trim();
            else if (bullet) text = "• " + text.Substring(2).TrimStart();
            text = Emphasis.Replace(Link.Replace(text, "$1"), "");

            // In Markdown a single line break inside a paragraph or list item is only a space;
            // joining such lines lets the text box wrap them to its own width.
            bool continues = !startsBlock && !previousWasHeading && lines.Count > 0 && lines[lines.Count - 1].Length > 0;
            if (continues) lines[lines.Count - 1] += " " + text;
            else lines.Add(text);

            previousWasHeading = heading;
        }

        return string.Join("\r\n", lines).Trim();
    }
}
