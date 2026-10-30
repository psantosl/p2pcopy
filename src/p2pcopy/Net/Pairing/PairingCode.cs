using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using P2pCopy.Net.Candidates;

namespace P2pCopy.Net.Pairing;

internal enum PeerRole : byte
{
    Receiver = 0,
    Sender = 1,
}

/// <summary>
/// Everything a peer needs to find and trust us, in one paste-able string:
/// role, NAT class, a random secret, and every address we might be reachable at.
/// </summary>
internal sealed class PairingCode
{
    public const int Version = 1;
    public const int SecretSize = 12;
    private const int ChecksumSize = 2;
    private const int MaxCandidates = 8;

    public PeerRole Role { get; }
    public NatKind Nat { get; }
    public byte[] Secret { get; }
    public IReadOnlyList<Candidate> Candidates { get; }

    public PairingCode(PeerRole role, NatKind nat, byte[] secret, IReadOnlyList<Candidate> candidates)
    {
        if (secret.Length != SecretSize) throw new ArgumentException($"Secret must be {SecretSize} bytes", nameof(secret));
        if (candidates.Count > MaxCandidates) candidates = candidates.Take(MaxCandidates).ToList();
        Role = role;
        Nat = nat;
        Secret = secret;
        Candidates = candidates;
    }

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretSize);

    public bool HasIpv6 => Candidates.Any(c => c.Kind == CandidateKind.Ipv6);

    /// <summary>Whether a peer holding only this code has a fair chance of connecting.</summary>
    public bool LikelyReachable => Nat == NatKind.None || HasIpv6 || Nat == NatKind.Cone;

    /// <summary>Short, stable identifier of the secret for logs and probe packets.</summary>
    public static uint SecretId(ReadOnlySpan<byte> secret)
    {
        var label = "p2pcopy-secret-id"u8;
        Span<byte> input = stackalloc byte[label.Length + SecretSize];
        label.CopyTo(input);
        secret.CopyTo(input.Slice(label.Length));
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadUInt32BigEndian(hash);
    }

    public byte[] ToBytes()
    {
        var size = 2 + SecretSize + 1 + Candidates.Sum(c => 3 + (c.Family == AddressFamily.InterNetworkV6 ? 16 : 4)) + ChecksumSize;
        var buf = new byte[size];
        var pos = 0;
        buf[pos++] = Version;
        buf[pos++] = (byte)(((byte)Role & 1) | (((byte)Nat & 7) << 1));
        Secret.CopyTo(buf, pos);
        pos += SecretSize;
        buf[pos++] = (byte)Candidates.Count;
        foreach (var c in Candidates)
        {
            var isV6 = c.Family == AddressFamily.InterNetworkV6;
            buf[pos++] = (byte)(((byte)c.Kind << 4) | (isV6 ? 6 : 4));
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(pos, 2), (ushort)c.EndPoint.Port);
            pos += 2;
            var addr = c.EndPoint.Address.GetAddressBytes();
            addr.CopyTo(buf, pos);
            pos += addr.Length;
        }
        var check = Checksum(buf.AsSpan(0, pos));
        buf[pos++] = check[0];
        buf[pos++] = check[1];
        return buf;
    }

    public string Encode() => Base32Crockford.Encode(ToBytes());

    /// <summary>Same as <see cref="Encode"/> with a dash every 6 characters for readability.</summary>
    public string EncodePretty()
    {
        var raw = Encode();
        var sb = new StringBuilder(raw.Length + raw.Length / 6);
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && i % 6 == 0) sb.Append('-');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    public static bool TryParse(string text, out PairingCode? code, out string? error)
    {
        code = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty code";
            return false;
        }
        if (!Base32Crockford.TryDecode(text, out var bytes))
        {
            error = "the code contains characters that are not part of a p2pcopy code";
            return false;
        }
        return TryParseBytes(bytes, out code, out error);
    }

    public static bool TryParseBytes(ReadOnlySpan<byte> buf, out PairingCode? code, out string? error)
    {
        code = null;
        error = null;
        if (buf.Length < 2 + SecretSize + 1 + ChecksumSize)
        {
            error = "the code is too short";
            return false;
        }
        var expected = Checksum(buf.Slice(0, buf.Length - ChecksumSize));
        if (buf[buf.Length - 2] != expected[0] || buf[buf.Length - 1] != expected[1])
        {
            error = "the code has a typo (checksum mismatch)";
            return false;
        }
        var pos = 0;
        var version = buf[pos++];
        if (version != Version)
        {
            error = $"the code was made by an incompatible p2pcopy (code version {version}, this build understands {Version})";
            return false;
        }
        var flags = buf[pos++];
        var role = (PeerRole)(flags & 1);
        var nat = (NatKind)((flags >> 1) & 7);
        var secret = buf.Slice(pos, SecretSize).ToArray();
        pos += SecretSize;
        int count = buf[pos++];
        var candidates = new List<Candidate>(count);
        for (var i = 0; i < count; i++)
        {
            if (pos + 3 > buf.Length - ChecksumSize) { error = "truncated candidate list"; return false; }
            var kindAndFamily = buf[pos++];
            var kind = (CandidateKind)(kindAndFamily >> 4);
            var family = kindAndFamily & 0x0F;
            var port = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(pos, 2));
            pos += 2;
            var addrLen = family == 6 ? 16 : family == 4 ? 4 : -1;
            if (addrLen < 0 || pos + addrLen > buf.Length - ChecksumSize) { error = "bad candidate address"; return false; }
            var address = new IPAddress(buf.Slice(pos, addrLen));
            pos += addrLen;
            candidates.Add(new Candidate(kind, new IPEndPoint(address, port)));
        }
        code = new PairingCode(role, nat, secret, candidates);
        return true;
    }

    private static byte[] Checksum(ReadOnlySpan<byte> payload)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return new[] { hash[0], hash[1] };
    }
}
