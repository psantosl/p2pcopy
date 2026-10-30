namespace P2pCopy.Ui;

internal static class Format
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Bytes(long bytes)
    {
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {Units[u]}";
    }

    public static string Speed(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 1) return $"{t.TotalMilliseconds:0} ms";
        if (t.TotalMinutes < 1) return $"{t.TotalSeconds:0.#} s";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m {t.Seconds:00}s";
        return $"{(int)t.TotalHours}h {t.Minutes:00}m";
    }

    public static string Eta(long done, long total, double bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || done >= total) return "--:--";
        var secs = (total - done) / bytesPerSecond;
        if (secs > 99 * 3600) return "--:--";
        var t = TimeSpan.FromSeconds(secs);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
