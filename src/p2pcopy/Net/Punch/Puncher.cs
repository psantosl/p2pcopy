using System.Net;
using System.Security.Cryptography;
using P2pCopy.Net.Pairing;
using P2pCopy.Net.Udp;

namespace P2pCopy.Net.Punch;

internal sealed record PunchResult(UdpHub Hub, IPEndPoint Remote);

/// <summary>
/// Sends authenticated probes to every remote candidate on every local socket until one
/// pair works in both directions. The sender ("controlling" side) then tells the receiver
/// which pair to use, and both hand that socket to UDT.
/// </summary>
internal sealed class Puncher : IDisposable
{
    private readonly IReadOnlyList<UdpHub> _hubs;
    private readonly PairingState _pairing;
    private readonly bool _controlling;
    private readonly Action<string>? _log;
    private readonly byte[] _nonce = RandomNumberGenerator.GetBytes(ProbeMessage.NonceSize);
    private readonly object _gate = new();
    private readonly HashSet<IPEndPoint> _remotes = new();
    private readonly HashSet<(UdpHub, IPEndPoint)> _validated = new();
    private readonly TaskCompletionSource<PunchResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IDisposable> _subscriptions = new();
    private bool _selecting;

    public Puncher(IReadOnlyList<UdpHub> hubs, PairingState pairing, bool controlling, Action<string>? log)
    {
        _hubs = hubs;
        _pairing = pairing;
        _controlling = controlling;
        _log = log;
        foreach (var hub in hubs)
        {
            var h = hub;
            _subscriptions.Add(hub.Subscribe((from, data) => OnDatagram(h, from, data)));
        }
    }

    public void AddRemotes(IEnumerable<IPEndPoint> remotes)
    {
        lock (_gate)
        {
            foreach (var r in remotes)
                if (_hubs.Any(h => h.Family == r.AddressFamily))
                    _remotes.Add(r);
        }
    }

    public int RemoteCount { get { lock (_gate) return _remotes.Count; } }

    public async Task<PunchResult?> RunAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sendLoop = SendLoopAsync(cts.Token);
        var finished = await Task.WhenAny(_done.Task, sendLoop).ConfigureAwait(false);
        cts.Cancel();
        try { await sendLoop.ConfigureAwait(false); } catch { }
        return finished == _done.Task ? await _done.Task.ConfigureAwait(false) : null;
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(250);
        while (!ct.IsCancellationRequested)
        {
            IPEndPoint[] remotes;
            lock (_gate) remotes = _remotes.ToArray();
            var secret = _pairing.TagSecret;
            var ping = ProbeMessage.Create(ProbeType.Ping, secret, _nonce, ReadOnlySpan<byte>.Empty);
            foreach (var hub in _hubs)
            {
                foreach (var r in remotes)
                {
                    if (r.AddressFamily != hub.Family) continue;
                    try { await hub.SendAsync(ping, r, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    catch { }
                }
            }
            try { await Task.Delay(interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private bool OnDatagram(UdpHub hub, IPEndPoint from, ReadOnlyMemory<byte> data)
    {
        if (_done.Task.IsCompleted) return false;

        if (!ProbeMessage.LooksLikeProbe(data.Span))
        {
            // Not ours. If the receiver side already validated this remote and now sees
            // something that is not a probe, it is almost certainly the sender's UDT
            // handshake: the sender has moved on, so follow it.
            if (!_controlling)
            {
                bool known;
                lock (_gate) known = _validated.Contains((hub, from));
                if (known)
                {
                    _log?.Invoke($"peer started talking UDT from {from}");
                    Complete(hub, from);
                    return false;
                }
            }
            return false;
        }

        if (!ProbeMessage.TryParse(data.Span, _pairing.KnownSecrets, out var type, out _, out var echo))
            return true;

        switch (type)
        {
            case ProbeType.Ping:
            {
                // Learn the real source address (it may differ from the advertised candidate).
                bool added;
                lock (_gate) added = _remotes.Add(from);
                if (added) _log?.Invoke($"probe from {from} via {hub.Family}");
                var pong = ProbeMessage.Create(ProbeType.Pong, _pairing.TagSecret, _nonce, data.Span.Slice(20, ProbeMessage.NonceSize));
                _ = hub.SendAsync(pong, from).AsTask();
                return true;
            }
            case ProbeType.Pong:
            {
                if (!echo.AsSpan().SequenceEqual(_nonce)) return true;
                bool first;
                lock (_gate)
                {
                    first = _validated.Add((hub, from)) && _validated.Count == 1;
                    _remotes.Add(from);
                }
                if (first) _log?.Invoke($"two-way path confirmed with {from}");
                if (_controlling) _ = SelectAsync(hub, from);
                return true;
            }
            case ProbeType.Select:
            {
                if (_controlling) return true;
                lock (_gate) _remotes.Add(from);
                _log?.Invoke($"peer selected path {from}");
                Complete(hub, from);
                return true;
            }
        }
        return true;
    }

    private async Task SelectAsync(UdpHub hub, IPEndPoint remote)
    {
        lock (_gate)
        {
            if (_selecting) return;
            _selecting = true;
        }
        var select = ProbeMessage.Create(ProbeType.Select, _pairing.TagSecret, _nonce, ReadOnlySpan<byte>.Empty);
        for (var i = 0; i < 5; i++)
        {
            try { await hub.SendAsync(select, remote).ConfigureAwait(false); } catch { }
            await Task.Delay(40).ConfigureAwait(false);
        }
        Complete(hub, remote);
    }

    private void Complete(UdpHub hub, IPEndPoint remote)
    {
        _done.TrySetResult(new PunchResult(hub, remote));
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
    }
}
