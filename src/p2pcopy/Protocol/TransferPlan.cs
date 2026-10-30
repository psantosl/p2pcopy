namespace P2pCopy.Protocol;

internal sealed class TransferPlanException : Exception
{
    public TransferPlanException(string message) : base(message) { }
}

/// <summary>Turns the paths given on the command line into a manifest.</summary>
internal static class TransferPlan
{
    public static Manifest Build(IEnumerable<string> paths)
    {
        var entries = new List<ManifestEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in paths)
        {
            var full = Path.GetFullPath(raw);
            if (File.Exists(full))
            {
                var info = new FileInfo(full);
                Add(entries, seen, new ManifestEntry(EntryKind.File, info.Name, info.Length, ToUnix(info.LastWriteTimeUtc)) { SourcePath = full });
            }
            else if (Directory.Exists(full))
            {
                var dir = new DirectoryInfo(full);
                var rootName = dir.Name;
                if (string.IsNullOrEmpty(rootName)) rootName = "root";
                Add(entries, seen, new ManifestEntry(EntryKind.Directory, rootName, 0, ToUnix(dir.LastWriteTimeUtc)) { SourcePath = full });
                Walk(dir, rootName, entries, seen);
            }
            else
            {
                throw new TransferPlanException($"'{raw}' does not exist.");
            }
        }

        if (entries.Count == 0) throw new TransferPlanException("Nothing to send.");
        return new Manifest { Entries = entries };
    }

    private static void Walk(DirectoryInfo dir, string wirePrefix, List<ManifestEntry> entries, HashSet<string> seen)
    {
        IEnumerable<FileSystemInfo> children;
        try { children = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
        catch (Exception ex) { throw new TransferPlanException($"Cannot read '{dir.FullName}': {ex.Message}"); }

        foreach (var child in children.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var wire = PathSanitizer.ToWirePath(wirePrefix, child.Name);
            if (child is DirectoryInfo d)
            {
                Add(entries, seen, new ManifestEntry(EntryKind.Directory, wire, 0, ToUnix(d.LastWriteTimeUtc)) { SourcePath = d.FullName });
                Walk(d, wire, entries, seen);
            }
            else if (child is FileInfo f)
            {
                Add(entries, seen, new ManifestEntry(EntryKind.File, wire, f.Length, ToUnix(f.LastWriteTimeUtc)) { SourcePath = f.FullName });
            }
        }
    }

    private static void Add(List<ManifestEntry> entries, HashSet<string> seen, ManifestEntry entry)
    {
        if (!seen.Add(entry.RelativePath))
            throw new TransferPlanException($"Two items would arrive with the same name '{entry.RelativePath}'. Send them from a parent directory instead.");
        entries.Add(entry);
    }

    private static long ToUnix(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();
}
