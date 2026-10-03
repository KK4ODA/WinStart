using System.Text;
using System.Text.RegularExpressions;

namespace WinStart.Update;

/// <summary>Turns the Markdown of a GitHub release description into readable plain text.</summary>
internal static class ReleaseNotes
{
    private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant);
    private static readonly Regex Emphasis = new Regex(@"(\*\*|__|`)", RegexOptions.CultureInvariant);

    public static string ToPlainText(string markdown)
    {
        var sb = new StringBuilder();
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            string indent = line.Substring(0, line.Length - trimmed.Length);

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                trimmed = trimmed.TrimStart('#').Trim();
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                trimmed = "• " + trimmed.Substring(2);

            trimmed = Link.Replace(trimmed, "$1");
            trimmed = Emphasis.Replace(trimmed, "");
            sb.Append(indent).Append(trimmed).Append("\r\n");
        }

        return sb.ToString().Trim();
    }
}
