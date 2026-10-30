using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace P2pCopy.Net.Candidates;

internal static class LocalAddresses
{
    /// <summary>Usable IPv4 addresses on up interfaces, private ranges first.</summary>
    public static IReadOnlyList<IPAddress> GetIpv4()
    {
        var list = new List<(IPAddress Address, int Rank)>();
        foreach (var (nic, uni) in EnumerateUnicast())
        {
            var a = uni.Address;
            if (a.AddressFamily != AddressFamily.InterNetwork) continue;
            if (IPAddress.IsLoopback(a)) continue;
            var b = a.GetAddressBytes();
            if (b[0] == 169 && b[1] == 254) continue; // link-local
            var rank = IsPrivateIpv4(b) ? 0 : 1;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (IsVirtualish(nic)) rank += 2;
            list.Add((a, rank));
        }
        return list.OrderBy(x => x.Rank).Select(x => x.Address).Distinct().ToList();
    }

    /// <summary>Global IPv6 addresses on up interfaces, stable (non-temporary) ones first.</summary>
    public static IReadOnlyList<IPAddress> GetGlobalIpv6()
    {
        var list = new List<(IPAddress Address, int Rank)>();
        foreach (var (nic, uni) in EnumerateUnicast())
        {
            var a = uni.Address;
            if (a.AddressFamily != AddressFamily.InterNetworkV6) continue;
            if (IPAddress.IsLoopback(a) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) continue;
            if (a.IsIPv6UniqueLocal) continue;
            if (a.IsIPv4MappedToIPv6) continue;
            var b = a.GetAddressBytes();
            if ((b[0] & 0xE0) != 0x20) continue; // only 2000::/3 is global unicast
            var rank = 0;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (uni.SuffixOrigin == SuffixOrigin.Random) rank += 1; // temporary privacy address
                    if (uni.DuplicateAddressDetectionState != DuplicateAddressDetectionState.Preferred &&
                        uni.DuplicateAddressDetectionState != DuplicateAddressDetectionState.Invalid)
                        rank += 2;
                }
                catch (PlatformNotSupportedException) { }
            }
            if (IsVirtualish(nic)) rank += 4;
            list.Add((a, rank));
        }
        return list.OrderBy(x => x.Rank).Select(x => x.Address).Distinct().ToList();
    }

    public static bool IsPrivateIpv4(ReadOnlySpan<byte> b) =>
        b[0] == 10 ||
        (b[0] == 172 && (b[1] & 0xF0) == 16) ||
        (b[0] == 192 && b[1] == 168) ||
        (b[0] == 100 && (b[1] & 0xC0) == 64); // CGNAT 100.64/10

    private static IEnumerable<(NetworkInterface Nic, UnicastIPAddressInformation Uni)> EnumerateUnicast()
    {
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { yield break; }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); } catch { continue; }
            foreach (var uni in props.UnicastAddresses)
                yield return (nic, uni);
        }
    }

    private static bool IsVirtualish(NetworkInterface nic)
    {
        var n = nic.Name + " " + nic.Description;
        return n.Contains("docker", StringComparison.OrdinalIgnoreCase)
            || n.Contains("veth", StringComparison.OrdinalIgnoreCase)
            || n.Contains("vbox", StringComparison.OrdinalIgnoreCase)
            || n.Contains("vmware", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || n.Contains("WSL", StringComparison.OrdinalIgnoreCase)
            || n.Contains("tailscale", StringComparison.OrdinalIgnoreCase)
            || n.Contains("utun", StringComparison.OrdinalIgnoreCase)
            || n.Contains("br-", StringComparison.OrdinalIgnoreCase);
    }
}
