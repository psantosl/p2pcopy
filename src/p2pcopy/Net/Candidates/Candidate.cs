using System.Net;
using System.Net.Sockets;

namespace P2pCopy.Net.Candidates;

internal enum CandidateKind : byte
{
    /// <summary>Private IPv4 address on a local interface.</summary>
    Lan = 1,
    /// <summary>Public IPv4 address and port as seen by a STUN server.</summary>
    Reflexive = 2,
    /// <summary>Global IPv6 address on a local interface.</summary>
    Ipv6 = 3,
}

internal sealed record Candidate(CandidateKind Kind, IPEndPoint EndPoint)
{
    public AddressFamily Family => EndPoint.AddressFamily;

    public string KindLabel => Kind switch
    {
        CandidateKind.Lan => "LAN",
        CandidateKind.Reflexive => "Public",
        CandidateKind.Ipv6 => "IPv6",
        _ => Kind.ToString(),
    };

    /// <summary>Lower is tried/preferred first.</summary>
    public int Priority => Kind switch
    {
        CandidateKind.Lan => 0,
        CandidateKind.Ipv6 => 1,
        CandidateKind.Reflexive => 2,
        _ => 9,
    };

    public override string ToString() => $"{KindLabel} {EndPoint}";
}

/// <summary>What we learned about the NAT in front of us. Encoded in the pairing code (3 bits).</summary>
internal enum NatKind : byte
{
    Unknown = 0,
    /// <summary>No NAT: the STUN mapped address is one of our own addresses.</summary>
    None = 1,
    /// <summary>Consistent mapping across servers: some flavour of cone NAT.</summary>
    Cone = 2,
    /// <summary>Mapping changes per destination: hole punching is unlikely to work.</summary>
    Symmetric = 3,
    /// <summary>No STUN server answered: UDP to the internet looks blocked.</summary>
    UdpBlocked = 4,
}

internal static class NatKindExtensions
{
    public static string Describe(this NatKind kind) => kind switch
    {
        NatKind.None => "no NAT (public address)",
        NatKind.Cone => "cone NAT",
        NatKind.Symmetric => "symmetric NAT",
        NatKind.UdpBlocked => "UDP blocked",
        _ => "unknown",
    };
}
