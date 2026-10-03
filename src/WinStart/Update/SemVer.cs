using System.Globalization;

namespace WinStart.Update;

/// <summary>A Semantic Versioning 2.0 version: MAJOR.MINOR.PATCH with an optional pre-release tag.</summary>
internal sealed class SemVer : IComparable<SemVer>
{
    public SemVer(int major, int minor, int patch, string[]? preRelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease ?? new string[0];
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Dot-separated pre-release identifiers, e.g. ["beta", "1"]; empty for a normal release.</summary>
    public string[] PreRelease { get; }

    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>Parses "1.2.3", "v1.2.3", "1.2" or "1.2.3-beta.1+build". Build metadata is ignored.</summary>
    public static bool TryParse(string? text, out SemVer? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text!.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);

        int plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);

        string[] pre = new string[0];
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s.Substring(dash + 1).Split('.');
            if (pre.Any(p => p.Length == 0)) return false;
            s = s.Substring(0, dash);
        }

        string[] parts = s.Split('.');
        if (parts.Length < 2 || parts.Length > 3) return false;

        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new SemVer(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        int result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // A pre-release sorts before the release it leads up to.
        if (!IsPreRelease || !other.IsPreRelease)
            return IsPreRelease == other.IsPreRelease ? 0 : (IsPreRelease ? -1 : 1);

        for (int i = 0; i < Math.Min(PreRelease.Length, other.PreRelease.Length); i++)
        {
            result = CompareIdentifiers(PreRelease[i], other.PreRelease[i]);
            if (result != 0) return result;
        }

        return PreRelease.Length.CompareTo(other.PreRelease.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPreRelease ? "-" + string.Join(".", PreRelease) : "");

    private static int CompareIdentifiers(string a, string b)
    {
        bool aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int x);
        bool bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out int y);
        if (aNumeric && bNumeric) return x.CompareTo(y);
        if (aNumeric != bNumeric) return aNumeric ? -1 : 1; // numeric identifiers sort first
        return Math.Sign(string.CompareOrdinal(a, b));
    }
}
