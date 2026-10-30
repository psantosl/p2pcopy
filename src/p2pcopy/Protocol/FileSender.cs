using System.Diagnostics;
using System.Security.Cryptography;

namespace P2pCopy.Protocol;

/// <summary>Sender side of the transfer: manifest, then every file streamed with no per-chunk acks.</summary>
internal static class FileSender
{
    private const int ChunkSize = SecureChannel.MaxPlaintext - 1;

    public static TransferSummary Run(SecureChannel channel, Manifest manifest, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        channel.WriteMessage(Messages.EncodeManifest(manifest));
        channel.Flush();

        using (var reply = channel.ReadMessage() ?? throw new EndOfStreamException("The peer went away before answering the manifest."))
        {
            var type = (MessageType)reply.Span[0];
            if (type == MessageType.Reject)
                throw new TransferRejectedException(Messages.DecodeText(reply.Span.Slice(1)));
            if (type != MessageType.Accept)
                throw new ProtocolViolationException($"Expected Accept, got {type}.");
        }

        var total = manifest.TotalBytes;
        var fileCount = manifest.FileCount;
        long done = 0;
        var fileIndex = 0;
        var chunk = new byte[1 + ChunkSize];
        Span<byte> digest = stackalloc byte[32];
        chunk[0] = (byte)MessageType.Data;

        for (var i = 0; i < manifest.Entries.Count; i++)
        {
            var entry = manifest.Entries[i];
            if (entry.Kind != EntryKind.File) continue;
            ct.ThrowIfCancellationRequested();
            fileIndex++;

            channel.WriteMessage(Messages.EncodeFileBegin((uint)i));
            progress.Report(new TransferProgress(done, total, fileIndex, fileCount, entry.RelativePath, 0, entry.Size, false));

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long fileDone = 0;
            using (var fs = new FileStream(entry.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            {
                var remaining = entry.Size;
                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var want = (int)Math.Min(ChunkSize, remaining);
                    var n = fs.Read(chunk, 1, want);
                    if (n <= 0) throw new IOException($"'{entry.RelativePath}' shrank while it was being sent.");
                    hash.AppendData(chunk, 1, n);
                    channel.WriteMessage(chunk.AsSpan(0, 1 + n));
                    remaining -= n;
                    fileDone += n;
                    done += n;
                    progress.Report(new TransferProgress(done, total, fileIndex, fileCount, entry.RelativePath, fileDone, entry.Size, false));
                }
            }

            hash.GetHashAndReset(digest);
            channel.WriteMessage(Messages.EncodeFileEnd(digest));
            progress.Report(new TransferProgress(done, total, fileIndex, fileCount, entry.RelativePath, fileDone, entry.Size, true));
        }

        channel.WriteMessage(Messages.EncodeSimple(MessageType.Done));
        channel.Flush();

        using var ack = channel.ReadMessage() ?? throw new EndOfStreamException("The peer went away before confirming the transfer.");
        var ackType = (MessageType)ack.Span[0];
        if (ackType == MessageType.Error)
            throw new ProtocolViolationException("Peer reported: " + Messages.DecodeText(ack.Span.Slice(1)));
        if (ackType != MessageType.DoneAck)
            throw new ProtocolViolationException($"Expected DoneAck, got {ackType}.");
        var (ok, message) = Messages.DecodeDoneAck(ack.Span.Slice(1));
        sw.Stop();
        return new TransferSummary(done, fileIndex, sw.Elapsed, ok, message);
    }
}
