using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;

namespace P2pCopy.Ui;

internal static class Diagnostics
{
    public static IEnumerable<string> Hints(ProbeResult own, PairingCode? peer)
    {
        if (peer == null)
        {
            yield return "Nobody connected and no peer code was entered. The other side needs your code, and if they are behind a NAT you need theirs too.";
            yield break;
        }

        if (own.Nat == NatKind.Symmetric || peer.Nat == NatKind.Symmetric)
        {
            var who = own.Nat == NatKind.Symmetric && peer.Nat == NatKind.Symmetric ? "Both sides are" : own.Nat == NatKind.Symmetric ? "Your side is" : "Your peer's side is";
            yield return $"{who} behind a symmetric NAT, which picks a new port for every destination. UDP hole punching rarely works through it.";
            yield return "Try from a different network: a phone hotspot is usually fine. Enabling UPnP on the router or using IPv6 also helps.";
        }

        if (own.Nat == NatKind.UdpBlocked || peer.Nat == NatKind.UdpBlocked)
            yield return "No STUN server answered on one side: outbound UDP looks blocked there. Only a LAN or IPv6 route can work then.";

        if (!own.HasIpv6 && !peer.HasIpv6)
            yield return "If both machines get IPv6 from their providers, the connection becomes direct and NAT stops mattering.";

        yield return "Retrying often works: NAT mappings and timing differ run to run. Swapping who sends and who receives can help too.";
        yield return "Run with --verbose to see each candidate and probe.";
    }
}
