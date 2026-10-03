using System.Security.Cryptography;

namespace WinStart.Update;

/// <summary>SHA-256 helpers for verifying downloaded installers against SHA256SUMS.txt.</summary>
internal static class Checksums
{
    /// <summary>
    /// Finds the checksum for <paramref name="fileName"/> in sha256sum-style text
    /// ("&lt;64 hex digits&gt;  &lt;file name&gt;" per line). Returns it in lower case, or null.
    /// </summary>
    public static string? Find(string sums, string fileName)
    {
        foreach (string raw in sums.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length < 66 || line[0] == '#') continue;

            string hash = line.Substring(0, 64);
            if (!char.IsWhiteSpace(line[64]) || !hash.All(Uri.IsHexDigit)) continue;

            string name = line.Substring(64).Trim().TrimStart('*'); // '*' marks binary mode
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return hash.ToLowerInvariant();
        }

        return null;
    }

    public static string Sha256OfFile(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
