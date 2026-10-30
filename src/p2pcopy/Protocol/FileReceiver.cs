using System.Diagnostics;
using System.Security.Cryptography;

namespace P2pCopy.Protocol;

internal sealed class IncomingTransfer
{
    public required Manifest Manifest { get; init; }
    /// <summary>Entry index to final local path.</summary>
    public required IReadOnlyDictionary<int, string> LocalPaths { get; init; }
    public required IReadOnlyList<string> AlreadyExisting { get; init; }
    public required IReadOnlyList<string> RejectedPaths { get; init; }
}

/// <summary>Receiver side: validate the manifest, ask the user, then write files as they stream in.</summary>
internal static class FileReceiver
{
    /// <summary>Step 1: read the manifest and work out where everything would land.</summary>
    public static IncomingTransfer ReadManifest(SecureChannel channel, string destinationDir)
    {
        Manifest manifest;
        using (var first = channel.ReadMessage() ?? throw new EndOfStreamException("The peer went away before sending the manifest."))
        {
            if ((MessageType)first.Span[0] != MessageType.Manifest)
                throw new ProtocolViolationException("Expected the manifest first.");
            manifest = Messages.DecodeManifest(first.Span.Slice(1));
        }
        return Plan(manifest, destinationDir);
    }

    /// <summary>Step 2a: tell the sender no.</summary>
    public static void Reject(SecureChannel channel, string reason)
    {
        channel.WriteMessage(Messages.EncodeText(MessageType.Reject, reason));
        channel.Flush();
    }

    /// <summary>Step 2b: accept and receive everything.</summary>
    public static TransferSummary Run(
        SecureChannel channel,
        IncomingTransfer incoming,
        string destinationDir,
        IProgress<TransferProgress> progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var manifest = incoming.Manifest;

        Directory.CreateDirectory(destinationDir);
        foreach (var (index, path) in incoming.LocalPaths)
        {
            if (manifest.Entries[index].Kind == EntryKind.Directory)
                Directory.CreateDirectory(path);
        }

        channel.WriteMessage(Messages.EncodeSimple(MessageType.Accept));
        channel.Flush();

        var total = manifest.TotalBytes;
        var fileCount = manifest.FileCount;
        long done = 0;
        var filesDone = 0;
        FileStream? current = null;
        IncrementalHash? hash = null;
        ManifestEntry? currentEntry = null;
        string? currentFinal = null;
        string? currentTemp = null;
        long currentDone = 0;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using var msg = channel.ReadMessage() ?? throw new EndOfStreamException("The connection dropped before the transfer finished.");
                var type = (MessageType)msg.Span[0];
                var payload = msg.Span.Slice(1);

                switch (type)
                {
                    case MessageType.FileBegin:
                    {
                        if (current != null) throw new ProtocolViolationException("FileBegin while a file was open.");
                        var index = (int)Messages.DecodeFileBegin(payload);
                        if (index < 0 || index >= manifest.Entries.Count || manifest.Entries[index].Kind != EntryKind.File)
                            throw new ProtocolViolationException("FileBegin for an unknown entry.");
                        currentEntry = manifest.Entries[index];
                        currentFinal = incoming.LocalPaths[index];
                        currentTemp = currentFinal + ".p2pcopy-part";
                        Directory.CreateDirectory(Path.GetDirectoryName(currentFinal)!);
                        current = new FileStream(currentTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan);
                        if (currentEntry.Size > 0)
                        {
                            try { current.SetLength(currentEntry.Size); } catch (IOException) { }
                        }
                        hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        currentDone = 0;
                        filesDone++;
                        progress.Report(new TransferProgress(done, total, filesDone, fileCount, currentEntry.RelativePath, 0, currentEntry.Size, false));
                        break;
                    }
                    case MessageType.Data:
                    {
                        if (current == null || hash == null || currentEntry == null) throw new ProtocolViolationException("Data without FileBegin.");
                        if (currentDone + payload.Length > currentEntry.Size) throw new ProtocolViolationException("More data than announced for " + currentEntry.RelativePath);
                        current.Write(payload);
                        hash.AppendData(payload);
                        currentDone += payload.Length;
                        done += payload.Length;
                        progress.Report(new TransferProgress(done, total, filesDone, fileCount, currentEntry.RelativePath, currentDone, currentEntry.Size, false));
                        break;
                    }
                    case MessageType.FileEnd:
                    {
                        if (current == null || hash == null || currentEntry == null || currentFinal == null || currentTemp == null)
                            throw new ProtocolViolationException("FileEnd without FileBegin.");
                        if (currentDone != currentEntry.Size)
                            throw new ProtocolViolationException($"'{currentEntry.RelativePath}' ended after {currentDone} of {currentEntry.Size} bytes.");
                        Span<byte> digest = stackalloc byte[32];
                        hash.GetHashAndReset(digest);
                        if (!digest.SequenceEqual(payload))
                        {
                            channel.WriteMessage(Messages.EncodeText(MessageType.Error, $"checksum mismatch on {currentEntry.RelativePath}"));
                            channel.Flush();
                            throw new IOException($"Checksum mismatch on '{currentEntry.RelativePath}'. The copy is corrupt.");
                        }
                        current.Dispose();
                        current = null;
                        hash.Dispose();
                        hash = null;
                        File.Move(currentTemp, currentFinal, overwrite: true);
                        try
                        {
                            File.SetLastWriteTimeUtc(currentFinal, DateTimeOffset.FromUnixTimeSeconds(currentEntry.ModifiedUnixSeconds).UtcDateTime);
                        }
                        catch { }
                        progress.Report(new TransferProgress(done, total, filesDone, fileCount, currentEntry.RelativePath, currentDone, currentEntry.Size, true));
                        currentEntry = null;
                        currentTemp = null;
                        currentFinal = null;
                        break;
                    }
                    case MessageType.Done:
                    {
                        if (current != null) throw new ProtocolViolationException("Done while a file was open.");
                        var ok = filesDone == fileCount && done == total;
                        var text = ok
                            ? $"{filesDone} file(s), {done} bytes, all checksums verified"
                            : $"received {filesDone} of {fileCount} files";
                        channel.WriteMessage(Messages.EncodeDoneAck(ok, text));
                        channel.Flush();
                        sw.Stop();
                        return new TransferSummary(done, filesDone, sw.Elapsed, ok, text);
                    }
                    case MessageType.Error:
                        throw new ProtocolViolationException("Peer reported: " + Messages.DecodeText(payload));
                    default:
                        throw new ProtocolViolationException($"Unexpected message {type}.");
                }
            }
        }
        finally
        {
            current?.Dispose();
            hash?.Dispose();
            if (currentTemp != null)
            {
                try { File.Delete(currentTemp); } catch { }
            }
        }
    }

    public static IncomingTransfer Plan(Manifest manifest, string destinationDir)
    {
        var paths = new Dictionary<int, string>();
        var existing = new List<string>();
        var rejected = new List<string>();
        var claimed = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var e = manifest.Entries[i];
            var local = PathSanitizer.ToLocalRelativePath(e.RelativePath, out _);
            var full = local == null ? null : PathSanitizer.ResolveInside(destinationDir, local);
            if (full == null)
            {
                rejected.Add(e.RelativePath);
                continue;
            }
            if (e.Kind == EntryKind.File && !claimed.Add(full))
            {
                rejected.Add(e.RelativePath);
                continue;
            }
            paths[i] = full;
            if (e.Kind == EntryKind.File && File.Exists(full)) existing.Add(e.RelativePath);
        }

        return new IncomingTransfer { Manifest = manifest, LocalPaths = paths, AlreadyExisting = existing, RejectedPaths = rejected };
    }
}
