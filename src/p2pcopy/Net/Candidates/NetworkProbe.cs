using System.Net;
using System.Net.Sockets;
using P2pCopy.Net.Stun;
using P2pCopy.Net.Udp;

namespace P2pCopy.Net.Candidates;

internal sealed class ProbeResult
{
    public required IReadOnlyList<Candidate> Candidates { get; init; }
    public required NatKind Nat { get; init; }
    public required IReadOnlyList<StunResult> StunResults { get; init; }
    public required int Port { get; init; }
    public bool HasIpv6 => Candidates.Any(c => c.Kind == CandidateKind.Ipv6);

    /// <summary>
    /// True when a peer that only has our code has a real chance of reaching us without
    /// us ever learning their address first. A cone NAT does not count: STUN cannot tell
    /// full cone from port-restricted cone, and most home routers are the latter.
    /// </summary>
    public bool LikelyReachable => Nat == NatKind.None || HasIpv6;
}

/// <summary>Gathers local candidates and asks STUN servers what the world sees.</summary>
internal static class NetworkProbe
{
    public static async Task<ProbeResult> RunAsync(
        UdpHub? v4,
        UdpHub? v6,
        IReadOnlyList<StunServer> stunServers,
        TimeSpan stunTimeout,
        Action<string>? status,
        CancellationToken ct)
    {
        var candidates = new List<Candidate>();
        var port = v4?.LocalPort ?? v6?.LocalPort ?? 0;

        if (v4 != null)
        {
            foreach (var a in LocalAddresses.GetIpv4().Take(3))
                candidates.Add(new Candidate(CandidateKind.Lan, new IPEndPoint(a, v4.LocalPort)));
        }

        if (v6 != null)
        {
            foreach (var a in LocalAddresses.GetGlobalIpv6().Take(2))
                candidates.Add(new Candidate(CandidateKind.Ipv6, new IPEndPoint(a, v6.LocalPort)));
        }

        var nat = NatKind.Unknown;
        IReadOnlyList<StunResult> stun = Array.Empty<StunResult>();

        if (v4 != null && stunServers.Count > 0)
        {
            status?.Invoke("Asking STUN servers for our public address…");
            stun = await StunClient.QueryAsync(v4, stunServers, stunTimeout, enoughResults: 2, ct).ConfigureAwait(false);

            if (stun.Count == 0)
            {
                nat = NatKind.UdpBlocked;
            }
            else
            {
                var mapped = stun.Select(r => r.Mapped).ToList();
                var distinct = mapped.Select(m => m.ToString()).Distinct().Count();
                var local = LocalAddresses.GetIpv4();

                if (stun.Count >= 2 && distinct > 1)
                {
                    nat = NatKind.Symmetric;
                }
                else if (local.Any(l => l.Equals(mapped[0].Address)) && mapped[0].Port == v4.LocalPort)
                {
                    nat = NatKind.None;
                }
                else
                {
                    nat = stun.Count >= 2 ? NatKind.Cone : NatKind.Unknown;
                }

                // Even a symmetric NAT's mapping is worth advertising: the peer may be the
                // one with the friendly NAT, and our outbound probes will fix up the port.
                foreach (var m in mapped.Select(m => m.ToString()).Distinct().Take(2))
                    candidates.Add(new Candidate(CandidateKind.Reflexive, IPEndPoint.Parse(m)));

                // A LAN candidate that equals the reflexive one is redundant.
                candidates.RemoveAll(c => c.Kind == CandidateKind.Lan && mapped.Any(m => m.Equals(c.EndPoint)));
            }
        }

        return new ProbeResult
        {
            Candidates = candidates.OrderBy(c => c.Priority).ToList(),
            Nat = nat,
            StunResults = stun,
            Port = port,
        };
    }
}
