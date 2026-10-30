using System.Net;
using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;
using Xunit;

namespace P2pCopy.Tests;

public class PairingCodeTests
{
    [Fact]
    public void Base32_RoundTrips_AndIsForgiving()
    {
        var data = new byte[] { 0, 1, 2, 250, 251, 252, 253, 254, 255, 42 };
        var text = Base32Crockford.Encode(data);
        Assert.True(Base32Crockford.TryDecode(text, out var back));
        Assert.Equal(data, back);

        // Lookalikes, case and separators are all accepted.
        var messy = text.ToUpperInvariant().Replace('0', 'O').Replace('1', 'I');
        messy = string.Join("-", Enumerable.Range(0, (messy.Length + 3) / 4).Select(i => messy.Substring(i * 4, Math.Min(4, messy.Length - i * 4))));
        Assert.True(Base32Crockford.TryDecode(messy, out var back2));
        Assert.Equal(data, back2);
    }

    [Fact]
    public void Code_RoundTrips_WithMixedCandidates()
    {
        var secret = PairingCode.NewSecret();
        var candidates = new[]
        {
            new Candidate(CandidateKind.Lan, new IPEndPoint(IPAddress.Parse("192.168.1.23"), 41222)),
            new Candidate(CandidateKind.Ipv6, new IPEndPoint(IPAddress.Parse("2a02:9b0:41c:1f3e::23"), 41222)),
            new Candidate(CandidateKind.Reflexive, new IPEndPoint(IPAddress.Parse("83.44.120.9"), 41222)),
        };
        var code = new PairingCode(PeerRole.Sender, NatKind.Cone, secret, candidates);
        var text = code.Encode();

        Assert.True(PairingCode.TryParse(text, out var parsed, out var error), error);
        Assert.Equal(PeerRole.Sender, parsed!.Role);
        Assert.Equal(NatKind.Cone, parsed.Nat);
        Assert.Equal(secret, parsed.Secret);
        Assert.Equal(candidates.Select(c => c.ToString()), parsed.Candidates.Select(c => c.ToString()));
        Assert.True(PairingCode.TryParse(code.EncodePretty(), out _, out _));
    }

    [Fact]
    public void Code_DetectsTypos()
    {
        var code = new PairingCode(PeerRole.Receiver, NatKind.None, PairingCode.NewSecret(),
            new[] { new Candidate(CandidateKind.Reflexive, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 5)) });
        var text = code.Encode().ToCharArray();
        text[5] = text[5] == 'a' ? 'b' : 'a';
        Assert.False(PairingCode.TryParse(new string(text), out _, out var error));
        Assert.Contains("typo", error);
        Assert.False(PairingCode.TryParse("", out _, out _));
        Assert.False(PairingCode.TryParse("not a code!!", out _, out _));
    }
}
