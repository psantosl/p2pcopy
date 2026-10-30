using System.Buffers.Binary;
using System.Security.Cryptography;
using P2pCopy.Net.Pairing;

namespace P2pCopy.Net.Punch;

internal enum ProbeType : byte
{
    Ping = 1,
    Pong = 2,
    Select = 3,
}

/// <summary>
/// Hole punching datagram. Laid out so that if one reaches a UDT socket it is dropped:
/// bytes 12..15 (UDT destination socket id) are all 0xFF, which UDT treats as "no such socket".
/// </summary>
internal static class ProbeMessage
{
    public const int Size = 52;
    private static ReadOnlySpan<byte> Magic => "P2PCPRB1"u8;
    public const int NonceSize = 8;
    private const int TagSize = 16;

    public static byte[] Create(ProbeType type, ReadOnlySpan<byte> secret, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> echoNonce)
    {
        var buf = new byte[Size];
        Magic.CopyTo(buf);
        buf[8] = (byte)type;
        buf[12] = buf[13] = buf[14] = buf[15] = 0xFF;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(16, 4), PairingCode.SecretId(secret));
        nonce.CopyTo(buf.AsSpan(20, NonceSize));
        if (!echoNonce.IsEmpty) echoNonce.CopyTo(buf.AsSpan(28, NonceSize));
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(secret, buf.AsSpan(0, 36), mac);
        mac.Slice(0, TagSize).CopyTo(buf.AsSpan(36, TagSize));
        return buf;
    }

    public static bool LooksLikeProbe(ReadOnlySpan<byte> data) =>
        data.Length == Size && data.Slice(0, 8).SequenceEqual(Magic);

    /// <summary>Verifies the tag against each known secret. Returns the parsed probe on success.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, IReadOnlyList<byte[]> knownSecrets, out ProbeType type, out byte[] nonce, out byte[] echoNonce)
    {
        type = default;
        nonce = Array.Empty<byte>();
        echoNonce = Array.Empty<byte>();
        if (!LooksLikeProbe(data)) return false;
        var id = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        Span<byte> mac = stackalloc byte[32];
        foreach (var secret in knownSecrets)
        {
            if (PairingCode.SecretId(secret) != id) continue;
            HMACSHA256.HashData(secret, data.Slice(0, 36), mac);
            if (!CryptographicOperations.FixedTimeEquals(mac.Slice(0, TagSize), data.Slice(36, TagSize))) continue;
            type = (ProbeType)data[8];
            nonce = data.Slice(20, NonceSize).ToArray();
            echoNonce = data.Slice(28, NonceSize).ToArray();
            return type is ProbeType.Ping or ProbeType.Pong or ProbeType.Select;
        }
        return false;
    }
}
