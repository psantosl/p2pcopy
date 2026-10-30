using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace P2pCopy.Net.Stun;

/// <summary>Minimal RFC 8489 STUN codec: Binding Request out, (XOR-)MAPPED-ADDRESS in.</summary>
internal static class StunMessage
{
    public const uint MagicCookie = 0x2112A442;
    public const int HeaderSize = 20;
    public const int TransactionIdSize = 12;

    private const ushort BindingRequest = 0x0001;
    private const ushort BindingSuccess = 0x0101;
    private const ushort AttrMappedAddress = 0x0001;
    private const ushort AttrXorMappedAddress = 0x0020;

    public static byte[] CreateBindingRequest(ReadOnlySpan<byte> transactionId)
    {
        if (transactionId.Length != TransactionIdSize)
            throw new ArgumentException("Transaction id must be 12 bytes", nameof(transactionId));

        var msg = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(0, 2), BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(msg.AsSpan(4, 4), MagicCookie);
        transactionId.CopyTo(msg.AsSpan(8, TransactionIdSize));
        return msg;
    }

    /// <summary>Returns true if the datagram is a STUN message with the given transaction id.</summary>
    public static bool IsResponseFor(ReadOnlySpan<byte> data, ReadOnlySpan<byte> transactionId)
    {
        if (data.Length < HeaderSize) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)) != MagicCookie) return false;
        return data.Slice(8, TransactionIdSize).SequenceEqual(transactionId);
    }

    public static bool TryParseBindingResponse(ReadOnlySpan<byte> data, out IPEndPoint? mapped)
    {
        mapped = null;
        if (data.Length < HeaderSize) return false;
        if ((data[0] & 0xC0) != 0) return false;

        var type = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0, 2));
        var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
        if (BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)) != MagicCookie) return false;
        if (type != BindingSuccess) return false;
        if (data.Length < HeaderSize + length) return false;

        var txId = data.Slice(8, TransactionIdSize);
        var attrs = data.Slice(HeaderSize, length);
        IPEndPoint? plain = null;

        var pos = 0;
        while (pos + 4 <= attrs.Length)
        {
            var attrType = BinaryPrimitives.ReadUInt16BigEndian(attrs.Slice(pos, 2));
            var attrLen = BinaryPrimitives.ReadUInt16BigEndian(attrs.Slice(pos + 2, 2));
            pos += 4;
            if (pos + attrLen > attrs.Length) break;
            var value = attrs.Slice(pos, attrLen);

            if (attrType == AttrXorMappedAddress && TryReadAddress(value, xor: true, txId, out var xored))
            {
                mapped = xored;
                return true;
            }
            if (attrType == AttrMappedAddress && TryReadAddress(value, xor: false, txId, out var p))
            {
                plain = p;
            }

            pos += (attrLen + 3) & ~3;
        }

        mapped = plain;
        return mapped != null;
    }

    private static bool TryReadAddress(ReadOnlySpan<byte> value, bool xor, ReadOnlySpan<byte> txId, out IPEndPoint? endPoint)
    {
        endPoint = null;
        if (value.Length < 4) return false;
        var family = value[1];
        var port = BinaryPrimitives.ReadUInt16BigEndian(value.Slice(2, 2));
        if (xor) port ^= (ushort)(MagicCookie >> 16);

        if (family == 0x01)
        {
            if (value.Length < 8) return false;
            Span<byte> addr = stackalloc byte[4];
            value.Slice(4, 4).CopyTo(addr);
            if (xor)
            {
                Span<byte> cookie = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(cookie, MagicCookie);
                for (var i = 0; i < 4; i++) addr[i] ^= cookie[i];
            }
            endPoint = new IPEndPoint(new IPAddress(addr), port);
            return true;
        }

        if (family == 0x02)
        {
            if (value.Length < 20) return false;
            Span<byte> addr = stackalloc byte[16];
            value.Slice(4, 16).CopyTo(addr);
            if (xor)
            {
                Span<byte> mask = stackalloc byte[16];
                BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
                txId.CopyTo(mask.Slice(4));
                for (var i = 0; i < 16; i++) addr[i] ^= mask[i];
            }
            endPoint = new IPEndPoint(new IPAddress(addr), port);
            return true;
        }

        return false;
    }
}
