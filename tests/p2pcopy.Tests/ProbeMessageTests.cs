using P2pCopy.Net.Pairing;
using P2pCopy.Net.Punch;
using Xunit;

namespace P2pCopy.Tests;

public class ProbeMessageTests
{
    [Fact]
    public void Probe_VerifiesWithKnownSecret_RejectsOthers()
    {
        var secret = PairingCode.NewSecret();
        var other = PairingCode.NewSecret();
        var nonce = new byte[ProbeMessage.NonceSize];
        nonce[0] = 7;
        var probe = ProbeMessage.Create(ProbeType.Ping, secret, nonce, ReadOnlySpan<byte>.Empty);

        Assert.Equal(ProbeMessage.Size, probe.Length);
        Assert.Equal(0xFF, probe[12]);
        Assert.True(ProbeMessage.LooksLikeProbe(probe));
        Assert.True(ProbeMessage.TryParse(probe, new[] { other, secret }, out var type, out var n, out _));
        Assert.Equal(ProbeType.Ping, type);
        Assert.Equal(nonce, n);
        Assert.False(ProbeMessage.TryParse(probe, new[] { other }, out _, out _, out _));

        probe[40] ^= 1;
        Assert.False(ProbeMessage.TryParse(probe, new[] { secret }, out _, out _, out _));
    }
}
