namespace WinStart.Core;

/// <summary>Turns delays in seconds into the text shown to the user.</summary>
internal static class DelayFormat
{
    /// <summary>"No delay", "30 sec", "1 min", "1 min 30 sec", "2 hr".</summary>
    public static string Describe(int seconds)
    {
        if (seconds <= 0) return "No delay";

        var parts = new List<string>(3);
        int hours = seconds / 3600, minutes = seconds % 3600 / 60, secs = seconds % 60;
        if (hours > 0) parts.Add(hours + " hr");
        if (minutes > 0) parts.Add(minutes + " min");
        if (secs > 0) parts.Add(secs + " sec");
        return string.Join(" ", parts);
    }

    /// <summary>"0:25", "4:05", "1:02:03".</summary>
    public static string Countdown(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }
}
