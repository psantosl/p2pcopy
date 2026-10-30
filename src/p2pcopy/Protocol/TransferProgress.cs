namespace P2pCopy.Protocol;

internal readonly record struct TransferProgress(
    long BytesDone,
    long TotalBytes,
    int FileIndex,
    int FileCount,
    string CurrentFile,
    long CurrentFileBytes,
    long CurrentFileSize,
    bool CurrentFileDone);

internal sealed record TransferSummary(long Bytes, int Files, TimeSpan Elapsed, bool Verified, string PeerMessage)
{
    public double BytesPerSecond => Elapsed.TotalSeconds > 0 ? Bytes / Elapsed.TotalSeconds : 0;
}
