using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using P2pCopy.Net.Pairing;

namespace P2pCopy.Net.Transport;

[Flags]
internal enum HelloFlags : byte
{
    None = 0,
    /// <summary>The sender of this HELLO pasted the other side's code, so it knows that secret.</summary>
    KnowsPeerSecret = 1,
    SupportsAesGcm = 2,
    SupportsChaCha20Poly1305 = 4,
}

/// <summary>
/// First bytes on every transport, in the clear: who we are and what we can do.
/// Fixed 64 bytes so the reader never has to guess.
/// </summary>
internal sealed class HelloMessage
{
    public const int Size = 64;
    public const ushort ProtocolVersion = 2;
    private static ReadOnlySpan<byte> Magic => "P2PCOPY2"u8;
    public const int NonceSize = 16;
    private const int MaxNameBytes = 32;

    public ushort Version { get; init; } = ProtocolVersion;
    public required PeerRole Role { get; init; }
    public required HelloFlags Flags { get; init; }
    public required byte[] Nonce { get; init; }
    public required string Name { get; init; }

    public static HelloMessage CreateOwn(PeerRole role, bool knowsPeerSecret)
    {
        var flags = HelloFlags.None;
        if (knowsPeerSecret) flags |= HelloFlags.KnowsPeerSecret;
        if (AesGcm.IsSupported) flags |= HelloFlags.SupportsAesGcm;
        if (ChaCha20Poly1305.IsSupported) flags |= HelloFlags.SupportsChaCha20Poly1305;
        var name = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(name)) name = "peer";
        return new HelloMessage
        {
            Role = role,
            Flags = flags,
            Nonce = RandomNumberGenerator.GetBytes(NonceSize),
            Name = name,
        };
    }

    public byte[] ToBytes()
    {
        var buf = new byte[Size];
        Magic.CopyTo(buf);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(8, 2), Version);
        buf[10] = (byte)Role;
        buf[11] = (byte)Flags;
        Nonce.CopyTo(buf, 12);
        var name = Encoding.UTF8.GetBytes(Name);
        var len = Math.Min(name.Length, MaxNameBytes);
        // Avoid cutting a multi-byte sequence in half.
        while (len > 0 && len < name.Length && (name[len] & 0xC0) == 0x80) len--;
        buf[28] = (byte)len;
        name.AsSpan(0, len).CopyTo(buf.AsSpan(29, MaxNameBytes));
        return buf;
    }

    public static bool TryParse(ReadOnlySpan<byte> buf, out HelloMessage? hello, out string? error)
    {
        hello = null;
        error = null;
        if (buf.Length < Size) { error = "short HELLO"; return false; }
        if (!buf.Slice(0, 8).SequenceEqual(Magic)) { error = "the peer is not p2pcopy"; return false; }
        var version = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(8, 2));
        if (version != ProtocolVersion) { error = $"the peer speaks protocol v{version}, this build speaks v{ProtocolVersion}"; return false; }
        var len = Math.Min((int)buf[28], MaxNameBytes);
        string name;
        try { name = Encoding.UTF8.GetString(buf.Slice(29, len)); }
        catch { name = "peer"; }
        hello = new HelloMessage
        {
            Version = version,
            Role = (PeerRole)(buf[10] & 1),
            Flags = (HelloFlags)buf[11],
            Nonce = buf.Slice(12, NonceSize).ToArray(),
            Name = name.Length == 0 ? "peer" : name,
        };
        return true;
    }
}
