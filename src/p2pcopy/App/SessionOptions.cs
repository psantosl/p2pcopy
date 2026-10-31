using P2pCopy.Net.Pairing;
using P2pCopy.Net.Stun;

namespace P2pCopy.App;

internal sealed class SessionOptions
{
    public required PeerRole Role { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
    public string OutputDirectory { get; init; } = ".";
    public string? PeerCode { get; init; }
    public int Port { get; init; }
    public bool Yes { get; init; }
    public bool NoClipboard { get; init; }
    public bool NoTcp { get; init; }
    public bool Ipv4Only { get; init; }
    public bool Verbose { get; init; }
    public IReadOnlyList<StunServer> StunServers { get; init; } = StunServer.Defaults;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(600);
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int Files = 2;
    public const int Connect = 3;
    public const int Transfer = 4;
    public const int Cancelled = 130;
}
