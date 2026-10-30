using System.Buffers.Binary;
using System.Net;
using P2pCopy.Net.Stun;
using Xunit;

namespace P2pCopy.Tests;

public class StunTests
{
    [Fact]
    public void BindingRequest_HasHeader()
    {
        var tx = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var req = StunMessage.CreateBindingRequest(tx);
        Assert.Equal(20, req.Length);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(0, 2)));
        Assert.Equal(StunMessage.MagicCookie, BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(4, 4)));
        Assert.True(StunMessage.IsResponseFor(req, tx));
    }

    [Fact]
    public void Parses_XorMappedAddress_IPv4()
    {
        var tx = new byte[12];
        var response = new byte[20 + 12];
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), 0x0101);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 12);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4, 4), StunMessage.MagicCookie);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(20, 2), 0x0020);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(22, 2), 8);
        response[25] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(26, 2), (ushort)(41222 ^ (StunMessage.MagicCookie >> 16)));
        var ip = IPAddress.Parse("83.44.120.9").GetAddressBytes();
        Span<byte> cookie = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(cookie, StunMessage.MagicCookie);
        for (var i = 0; i < 4; i++) response[28 + i] = (byte)(ip[i] ^ cookie[i]);

        Assert.True(StunMessage.TryParseBindingResponse(response, out var mapped));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("83.44.120.9"), 41222), mapped);
    }

    [Fact]
    public void Server_Parse()
    {
        Assert.Equal(new StunServer("stun.l.google.com", 19302), StunServer.Parse("stun.l.google.com:19302"));
        Assert.Equal(new StunServer("stun.example.org", 3478), StunServer.Parse("stun.example.org"));
    }
}
