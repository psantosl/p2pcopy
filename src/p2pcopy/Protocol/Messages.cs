using System.Buffers.Binary;
using System.Text;

namespace P2pCopy.Protocol;

internal enum MessageType : byte
{
    Manifest = 0x01,
    Accept = 0x02,
    Reject = 0x03,
    FileBegin = 0x10,
    Data = 0x11,
    FileEnd = 0x12,
    Done = 0x20,
    DoneAck = 0x21,
    Error = 0x7F,
}

internal enum EntryKind : byte
{
    File = 1,
    Directory = 2,
}

/// <summary>One item in the transfer. Paths are relative, '/' separated, never absolute.</summary>
internal sealed record ManifestEntry(EntryKind Kind, string RelativePath, long Size, long ModifiedUnixSeconds)
{
    /// <summary>Sender only: where the bytes come from.</summary>
    public string? SourcePath { get; init; }
}

internal sealed class Manifest
{
    public required IReadOnlyList<ManifestEntry> Entries { get; init; }
    public long TotalBytes => Entries.Where(e => e.Kind == EntryKind.File).Sum(e => e.Size);
    public int FileCount => Entries.Count(e => e.Kind == EntryKind.File);
    public int DirectoryCount => Entries.Count(e => e.Kind == EntryKind.Directory);
}

internal sealed class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message) : base(message) { }
}

internal sealed class TransferRejectedException : Exception
{
    public TransferRejectedException(string message) : base(message) { }
}

/// <summary>Hand-rolled little-endian message codec. Nothing here needs reflection.</summary>
internal static class Messages
{
    public static byte[] EncodeManifest(Manifest manifest)
    {
        var w = new Writer();
        w.Byte((byte)MessageType.Manifest);
        w.UInt32((uint)manifest.Entries.Count);
        foreach (var e in manifest.Entries)
        {
            w.Byte((byte)e.Kind);
            w.String(e.RelativePath);
            w.Int64(e.Size);
            w.Int64(e.ModifiedUnixSeconds);
        }
        return w.ToArray();
    }

    public static Manifest DecodeManifest(ReadOnlySpan<byte> payload)
    {
        var r = new Reader(payload);
        var count = r.UInt32();
        if (count > 1_000_000) throw new ProtocolViolationException("manifest too large");
        var entries = new List<ManifestEntry>((int)count);
        for (var i = 0; i < count; i++)
        {
            var kind = (EntryKind)r.Byte();
            var path = r.String();
            var size = r.Int64();
            var mtime = r.Int64();
            if (kind != EntryKind.File && kind != EntryKind.Directory) throw new ProtocolViolationException("bad entry kind");
            if (size < 0) throw new ProtocolViolationException("negative size");
            entries.Add(new ManifestEntry(kind, path, size, mtime));
        }
        return new Manifest { Entries = entries };
    }

    public static byte[] EncodeSimple(MessageType type) => new[] { (byte)type };

    public static byte[] EncodeText(MessageType type, string text)
    {
        var w = new Writer();
        w.Byte((byte)type);
        w.String(text);
        return w.ToArray();
    }

    public static string DecodeText(ReadOnlySpan<byte> payload) => new Reader(payload).String();

    public static byte[] EncodeFileBegin(uint index)
    {
        var buf = new byte[5];
        buf[0] = (byte)MessageType.FileBegin;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), index);
        return buf;
    }

    public static uint DecodeFileBegin(ReadOnlySpan<byte> payload) => BinaryPrimitives.ReadUInt32LittleEndian(payload);

    public static byte[] EncodeFileEnd(ReadOnlySpan<byte> sha256)
    {
        var buf = new byte[1 + 32];
        buf[0] = (byte)MessageType.FileEnd;
        sha256.CopyTo(buf.AsSpan(1));
        return buf;
    }

    public static byte[] EncodeDoneAck(bool ok, string message)
    {
        var w = new Writer();
        w.Byte((byte)MessageType.DoneAck);
        w.Byte(ok ? (byte)1 : (byte)0);
        w.String(message);
        return w.ToArray();
    }

    public static (bool Ok, string Message) DecodeDoneAck(ReadOnlySpan<byte> payload)
    {
        var r = new Reader(payload);
        var ok = r.Byte() == 1;
        return (ok, r.String());
    }

    private sealed class Writer
    {
        private readonly MemoryStream _ms = new();
        private readonly byte[] _tmp = new byte[8];
        public void Byte(byte b) => _ms.WriteByte(b);
        public void UInt32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(_tmp, v); _ms.Write(_tmp, 0, 4); }
        public void Int64(long v) { BinaryPrimitives.WriteInt64LittleEndian(_tmp, v); _ms.Write(_tmp, 0, 8); }
        public void String(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > ushort.MaxValue) throw new ArgumentException("string too long");
            BinaryPrimitives.WriteUInt16LittleEndian(_tmp, (ushort)bytes.Length);
            _ms.Write(_tmp, 0, 2);
            _ms.Write(bytes);
        }
        public byte[] ToArray() => _ms.ToArray();
    }

    private ref struct Reader
    {
        private ReadOnlySpan<byte> _s;
        public Reader(ReadOnlySpan<byte> s) { _s = s; }
        private ReadOnlySpan<byte> Take(int n)
        {
            if (_s.Length < n) throw new ProtocolViolationException("truncated message");
            var r = _s.Slice(0, n);
            _s = _s.Slice(n);
            return r;
        }
        public byte Byte() => Take(1)[0];
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public string String()
        {
            var len = BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
            return Encoding.UTF8.GetString(Take(len));
        }
    }
}
