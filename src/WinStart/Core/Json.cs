using System.Collections;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace WinStart.Core;

/// <summary>
/// Minimal JSON support using only what ships with .NET Framework: parsing through
/// <see cref="JavaScriptSerializer"/>, and a small writer that produces clean, indented text.
/// All JSON handling goes through this class so it can be swapped in one place.
/// </summary>
internal static class Json
{
    /// <summary>Parses JSON into dictionaries, arrays, strings, numbers, booleans and null.</summary>
    public static object? Parse(string text) =>
        new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text);

    /// <summary>Parses JSON whose root must be an object.</summary>
    public static IDictionary<string, object?> ParseObject(string text) =>
        Parse(text) as IDictionary<string, object?> ?? throw new FormatException("Expected a JSON object.");

    public static string GetString(this IDictionary<string, object?> obj, string key, string fallback = "") =>
        obj.TryGetValue(key, out object? value) && value is string text ? text : fallback;

    public static bool GetBool(this IDictionary<string, object?> obj, string key, bool fallback) =>
        obj.TryGetValue(key, out object? value) && value is bool flag ? flag : fallback;

    public static long GetLong(this IDictionary<string, object?> obj, string key, long fallback)
    {
        if (!obj.TryGetValue(key, out object? value) || value is null || value is string || value is bool)
            return fallback;
        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static int GetInt(this IDictionary<string, object?> obj, string key, int fallback) =>
        (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, obj.GetLong(key, fallback)));

    public static IDictionary<string, object?>? GetObject(this IDictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out object? value) ? value as IDictionary<string, object?> : null;

    public static IEnumerable<object?> GetArray(this IDictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out object? value) && value is IEnumerable list && value is not string
            ? list.Cast<object?>()
            : Enumerable.Empty<object?>();

    /// <summary>
    /// Writes dictionaries, lists, strings, numbers, booleans and null as JSON.
    /// Dictionary entries are written in insertion order.
    /// </summary>
    public static string Write(object? value, bool indented = true)
    {
        var sb = new StringBuilder();
        WriteValue(sb, value, indented, 0);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object? value, bool indented, int depth)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string text:
                WriteString(sb, text);
                break;
            case bool flag:
                sb.Append(flag ? "true" : "false");
                break;
            case int or long or short or byte or uint:
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;
            case double or float or decimal:
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                break;
            case IDictionary<string, object?> map:
                WriteObject(sb, map, indented, depth);
                break;
            case IEnumerable list:
                WriteArray(sb, list, indented, depth);
                break;
            default:
                throw new NotSupportedException("Cannot write " + value.GetType().Name + " as JSON.");
        }
    }

    private static void WriteObject(StringBuilder sb, IDictionary<string, object?> map, bool indented, int depth)
    {
        sb.Append('{');
        bool first = true;
        foreach (KeyValuePair<string, object?> pair in map)
        {
            if (!first) sb.Append(',');
            first = false;
            NewLine(sb, indented, depth + 1);
            WriteString(sb, pair.Key);
            sb.Append(indented ? ": " : ":");
            WriteValue(sb, pair.Value, indented, depth + 1);
        }

        if (!first) NewLine(sb, indented, depth);
        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, IEnumerable list, bool indented, int depth)
    {
        sb.Append('[');
        bool first = true;
        foreach (object? item in list)
        {
            if (!first) sb.Append(',');
            first = false;
            NewLine(sb, indented, depth + 1);
            WriteValue(sb, item, indented, depth + 1);
        }

        if (!first) NewLine(sb, indented, depth);
        sb.Append(']');
    }

    private static void NewLine(StringBuilder sb, bool indented, int depth)
    {
        if (!indented) return;
        sb.Append("\r\n").Append(' ', depth * 2);
    }

    private static void WriteString(StringBuilder sb, string text)
    {
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }
}
