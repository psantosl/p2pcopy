using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using P2pCopy.Net.Pairing;

namespace P2pCopy.Protocol;

internal sealed class PairingMismatchException : Exception
{
    public PairingMismatchException(string message) : base(message) { }
}

/// <summary>
/// Length-prefixed AEAD frames over any stream. Each direction has its own nonce counter,
/// so the same key is safe for both. There are no application acks: a frame is written and
/// the transport's own flow control does the rest.
/// </summary>
internal sealed class SecureChannel : IDisposable
{
    public const int MaxPlaintext = 256 * 1024;
    private const int TagSize = 16;
    private const int NonceSize = 12;
    private const int MaxFrame = MaxPlaintext + TagSize + 16;

    private readonly Stream _inner;
    private readonly AesGcm? _aes;
    private readonly ChaCha20Poly1305? _chacha;
    private readonly byte[] _sendNonce = new byte[NonceSize];
    private readonly byte[] _recvNonce = new byte[NonceSize];
    private ulong _sendCounter;
    private ulong _recvCounter;
    private readonly byte[] _lengthBuf = new byte[4];
    private readonly byte[] _frameBuf = new byte[MaxFrame];

    public SecureChannel(Stream inner, byte[] key, AeadKind kind, PeerRole myRole)
    {
        _inner = inner;
        if (kind == AeadKind.AesGcm) _aes = new AesGcm(key, TagSize);
        else _chacha = new ChaCha20Poly1305(key);
        _sendNonce[0] = (byte)(myRole == PeerRole.Sender ? 1 : 2);
        _recvNonce[0] = (byte)(myRole == PeerRole.Sender ? 2 : 1);
    }

    public string CipherName => _aes != null ? "AES-256-GCM" : "ChaCha20-Poly1305";

    public void WriteMessage(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length > MaxPlaintext) throw new ArgumentOutOfRangeException(nameof(plaintext));
        var frameLen = plaintext.Length + TagSize;
        var cipher = _frameBuf.AsSpan(0, plaintext.Length);
        var tag = _frameBuf.AsSpan(plaintext.Length, TagSize);

        BinaryPrimitives.WriteUInt64LittleEndian(_sendNonce.AsSpan(1, 8), _sendCounter++);
        if (_aes != null) _aes.Encrypt(_sendNonce, plaintext, cipher, tag);
        else _chacha!.Encrypt(_sendNonce, plaintext, cipher, tag);

        BinaryPrimitives.WriteInt32LittleEndian(_lengthBuf, frameLen);
        _inner.Write(_lengthBuf);
        _inner.Write(_frameBuf, 0, frameLen);
    }

    public void Flush() => _inner.Flush();

    /// <summary>Reads the next message into a pooled buffer. Returns null at end of stream.</summary>
    public PooledMessage? ReadMessage()
    {
        if (!TryReadExactly(_lengthBuf)) return null;
        var frameLen = BinaryPrimitives.ReadInt32LittleEndian(_lengthBuf);
        if (frameLen < TagSize || frameLen > MaxFrame)
            throw new PairingMismatchException("Received a malformed frame. The pairing codes probably do not match.");

        if (!TryReadExactly(_frameBuf.AsSpan(0, frameLen)))
            throw new EndOfStreamException("The connection closed in the middle of a message.");

        var plainLen = frameLen - TagSize;
        var owner = ArrayPool<byte>.Shared.Rent(Math.Max(plainLen, 1));
        BinaryPrimitives.WriteUInt64LittleEndian(_recvNonce.AsSpan(1, 8), _recvCounter++);
        try
        {
            if (_aes != null) _aes.Decrypt(_recvNonce, _frameBuf.AsSpan(0, plainLen), _frameBuf.AsSpan(plainLen, TagSize), owner.AsSpan(0, plainLen));
            else _chacha!.Decrypt(_recvNonce, _frameBuf.AsSpan(0, plainLen), _frameBuf.AsSpan(plainLen, TagSize), owner.AsSpan(0, plainLen));
        }
        catch (CryptographicException)
        {
            ArrayPool<byte>.Shared.Return(owner);
            throw new PairingMismatchException("Could not decrypt the peer's data. The pairing codes do not match, or someone is tampering with the connection.");
        }
        return new PooledMessage(owner, plainLen);
    }

    private bool TryReadExactly(Span<byte> target)
    {
        var read = 0;
        while (read < target.Length)
        {
            var n = _inner.Read(target.Slice(read));
            if (n <= 0) return read == 0 ? false : throw new EndOfStreamException("The connection closed in the middle of a message.");
            read += n;
        }
        return true;
    }

    public void Dispose()
    {
        _aes?.Dispose();
        _chacha?.Dispose();
    }
}

internal sealed class PooledMessage : IDisposable
{
    private byte[]? _buffer;
    public int Length { get; }

    public PooledMessage(byte[] buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    public ReadOnlySpan<byte> Span => _buffer!.AsSpan(0, Length);

    public void Dispose()
    {
        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }
}
