using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using P2pCopy.Net.Udp;

namespace P2pCopy.Net.Stun;

internal sealed record StunServer(string Host, int Port)
{
    public static readonly IReadOnlyList<StunServer> Defaults = new[]
    {
        new StunServer("stun.l.google.com", 19302),
        new StunServer("stun.cloudflare.com", 3478),
        new StunServer("stun1.l.google.com", 19302),
        new StunServer("stun.nextcloud.com", 443),
        new StunServer("stun.sipgate.net", 3478),
        new StunServer("stun.stunprotocol.org", 3478),
    };

    public static StunServer Parse(string text)
    {
        var s = text.Trim();
        var idx = s.LastIndexOf(':');
        if (idx > 0 && idx < s.Length - 1 && int.TryParse(s.AsSpan(idx + 1), out var port) && !s.Contains(']'))
            return new StunServer(s.Substring(0, idx), port);
        return new StunServer(s, 3478);
    }

    public override string ToString() => $"{Host}:{Port}";
}

internal sealed record StunResult(StunServer Server, IPEndPoint Mapped, TimeSpan Rtt);

/// <summary>
/// Sends one Binding Request per server, all at once, on the shared UDP socket and
/// collects whatever answers come back within the timeout.
/// </summary>
internal static class StunClient
{
    public static async Task<IReadOnlyList<StunResult>> QueryAsync(
        UdpHub hub,
        IEnumerable<StunServer> servers,
        TimeSpan timeout,
        int enoughResults,
        CancellationToken ct)
    {
        var tasks = servers.Select(s => QueryOneAsync(hub, s, timeout, ct)).ToList();
        var results = new List<StunResult>();

        while (tasks.Count > 0 && results.Count < enoughResults)
        {
            var done = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(done);
            var r = await done.ConfigureAwait(false);
            if (r != null) results.Add(r);
        }

        return results;
    }

    public static async Task<StunResult?> QueryOneAsync(UdpHub hub, StunServer server, TimeSpan timeout, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(server.Host, hub.Family, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
        var address = addresses.FirstOrDefault(a => a.AddressFamily == hub.Family);
        if (address == null) return null;
        var target = new IPEndPoint(address, server.Port);

        var txId = RandomNumberGenerator.GetBytes(StunMessage.TransactionIdSize);
        var request = StunMessage.CreateBindingRequest(txId);
        var tcs = new TaskCompletionSource<IPEndPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = hub.Subscribe((from, data) =>
        {
            if (!StunMessage.IsResponseFor(data.Span, txId)) return false;
            StunMessage.TryParseBindingResponse(data.Span, out var mapped);
            tcs.TrySetResult(mapped);
            return true;
        });

        var sw = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        using var reg = timeoutCts.Token.Register(() => tcs.TrySetResult(null));

        try
        {
            // Two attempts, spaced out, within the overall timeout.
            await hub.SendAsync(request, target, timeoutCts.Token).ConfigureAwait(false);
            var first = await Task.WhenAny(tcs.Task, Task.Delay(timeout / 2, timeoutCts.Token)).ConfigureAwait(false);
            if (first != tcs.Task)
                await hub.SendAsync(request, target, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }

        var result = await tcs.Task.ConfigureAwait(false);
        return result == null ? null : new StunResult(server, result, sw.Elapsed);
    }
}
