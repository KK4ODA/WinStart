using System.Globalization;
using System.Text;

namespace WinStart.Core;

internal sealed class HistoryRecord
{
    public DateTime Time { get; set; }
    public string App { get; set; } = "";
    public string Result { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>
/// The startup history: one JSON object per line in history.jsonl, oldest first.
/// It records what was launched, when, and whether it worked.
/// </summary>
internal sealed class History
{
    public const string Launched = "Launched";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
    public const string Info = "Info";

    private const int MaxRecords = 1000;
    private readonly object _gate = new object();
    private readonly string _path;

    public History(string path)
    {
        _path = path;
    }

    public static History Default { get; } = new History(AppPaths.HistoryFile);

    public void Add(string app, string result, string detail = "")
    {
        try
        {
            var record = new Dictionary<string, object?>
            {
                ["time"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                ["app"] = app,
                ["result"] = result,
                ["detail"] = detail,
            };
            lock (_gate)
                File.AppendAllText(_path, Json.Write(record, indented: false) + "\r\n", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error("Could not write the startup history", ex);
        }
    }

    /// <summary>Returns all records, oldest first. Lines that cannot be read are skipped.</summary>
    public List<HistoryRecord> Read()
    {
        var records = new List<HistoryRecord>();
        string[] lines;
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return records;
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the startup history", ex);
            return records;
        }

        foreach (string line in lines)
        {
            if (line.Trim().Length == 0) continue;
            try
            {
                IDictionary<string, object?> json = Json.ParseObject(line);
                if (!DateTimeOffset.TryParse(json.GetString("time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time))
                    continue;
                records.Add(new HistoryRecord
                {
                    Time = time.LocalDateTime,
                    App = json.GetString("app"),
                    Result = json.GetString("result"),
                    Detail = json.GetString("detail"),
                });
            }
            catch (Exception)
            {
                // A damaged line must not hide the rest of the history.
            }
        }

        return records;
    }

    public void Clear()
    {
        try
        {
            lock (_gate)
                File.Delete(_path);
        }
        catch (Exception ex)
        {
            Log.Error("Could not clear the startup history", ex);
        }
    }

    /// <summary>Drops the oldest records once the file grows past its limit.</summary>
    public void Trim(int keep = MaxRecords)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return;
                string[] lines = File.ReadAllLines(_path, Encoding.UTF8);
                if (lines.Length <= keep) return;
                File.WriteAllLines(_path, lines.Skip(lines.Length - keep), new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not trim the startup history", ex);
        }
    }
}
