namespace P2pCopy.Protocol;

/// <summary>
/// The sender chooses the relative paths; the receiver must not trust them. Anything that
/// could escape the output directory or is illegal on this OS is rejected or neutralised.
/// </summary>
internal static class PathSanitizer
{
    private static readonly HashSet<string> WindowsReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Returns the local relative path (OS separators) or null when the path is not acceptable.</summary>
    public static string? ToLocalRelativePath(string relativePath, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(relativePath)) { error = "empty path"; return null; }
        if (relativePath.Length > 4096) { error = "path too long"; return null; }
        if (relativePath.StartsWith('/') || relativePath.StartsWith('\\')) { error = "absolute path"; return null; }
        if (relativePath.Length >= 2 && relativePath[1] == ':') { error = "drive letter"; return null; }

        var raw = relativePath.Split('/', '\\');
        var segments = new List<string>(raw.Length);
        foreach (var seg in raw)
        {
            if (seg.Length == 0) continue;
            if (seg == "." ) continue;
            if (seg == "..") { error = "path escapes the destination (..)"; return null; }
            segments.Add(CleanSegment(seg));
        }
        if (segments.Count == 0) { error = "empty path"; return null; }
        return Path.Combine(segments.ToArray());
    }

    /// <summary>Combines and verifies the result is still inside the destination directory.</summary>
    public static string? ResolveInside(string destinationDir, string localRelative)
    {
        var root = Path.GetFullPath(destinationDir);
        var full = Path.GetFullPath(Path.Combine(root, localRelative));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(rootWithSep, comparison) ? full : null;
    }

    private static string CleanSegment(string seg)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = seg.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] < 0x20) chars[i] = '_';
        }
        var cleaned = new string(chars);

        if (OperatingSystem.IsWindows())
        {
            cleaned = cleaned.TrimEnd(' ', '.');
            if (cleaned.Length == 0) cleaned = "_";
            var stem = cleaned.Split('.')[0];
            if (WindowsReserved.Contains(stem)) cleaned = "_" + cleaned;
        }
        if (cleaned.Length > 255) cleaned = cleaned.Substring(0, 255);
        return cleaned;
    }

    /// <summary>Builds the wire path for a local file inside a root being sent.</summary>
    public static string ToWirePath(params string[] segments) => string.Join('/', segments);
}
