using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace P2pCopy.Net.Transport;

/// <summary>
/// TCP as a second route: listen on our port and, at the same time, connect to every
/// remote candidate from that same port (TCP simultaneous open). Whatever connects first
/// is offered to the connection race.
/// </summary>
internal sealed class TcpRacer : IDisposable
{
    private readonly int _port;
    private readonly Action<string>? _log;
    private readonly Channel<(Socket Socket, string How)> _connections = Channel.CreateUnbounded<(Socket, string)>();
    private readonly List<Socket> _listeners = new();
    private readonly HashSet<IPEndPoint> _remotes = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public TcpRacer(int port, bool v4, bool v6, Action<string>? log)
    {
        _port = port;
        _log = log;
        if (v4) TryListen(AddressFamily.InterNetwork);
        if (v6) TryListen(AddressFamily.InterNetworkV6);
    }

    public ChannelReader<(Socket Socket, string How)> Connections => _connections.Reader;

    public bool HasListener => _listeners.Count > 0;

    private void TryListen(AddressFamily family)
    {
        try
        {
            var s = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            if (family == AddressFamily.InterNetworkV6) s.DualMode = false;
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.Bind(new IPEndPoint(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, _port));
            s.Listen(4);
            _listeners.Add(s);
        }
        catch (SocketException ex)
        {
            _log?.Invoke($"tcp listen {family} on {_port} failed: {ex.SocketErrorCode}");
        }
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        foreach (var l in _listeners)
            _ = AcceptLoopAsync(l, _cts.Token);
    }

    public void AddRemotes(IEnumerable<IPEndPoint> remotes)
    {
        List<IPEndPoint> fresh = new();
        lock (_gate)
        {
            foreach (var r in remotes)
                if (_remotes.Add(r)) fresh.Add(r);
        }
        foreach (var r in fresh)
            _ = ConnectLoopAsync(r, _cts.Token);
    }

    private async Task AcceptLoopAsync(Socket listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener.AcceptAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            client.NoDelay = true;
            _log?.Invoke($"tcp accepted from {client.RemoteEndPoint}");
            _connections.Writer.TryWrite((client, "TCP (accepted)"));
        }
    }

    private async Task ConnectLoopAsync(IPEndPoint remote, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            var s = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                s.NoDelay = true;
                if (remote.AddressFamily == AddressFamily.InterNetworkV6) s.DualMode = false;
                try
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    s.Bind(new IPEndPoint(remote.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, _port));
                }
                catch (SocketException)
                {
                    // Could not share the port; an ephemeral one still works against a listener.
                }

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(TimeSpan.FromSeconds(4));
                await s.ConnectAsync(remote, attemptCts.Token).ConfigureAwait(false);
                _log?.Invoke($"tcp connected to {remote} (attempt {attempt})");
                _connections.Writer.TryWrite((s, "TCP (connected)"));
                return;
            }
            catch (OperationCanceledException)
            {
                s.Dispose();
                if (ct.IsCancellationRequested) return;
            }
            catch (SocketException)
            {
                s.Dispose();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var l in _listeners)
        {
            try { l.Dispose(); } catch { }
        }
        _connections.Writer.TryComplete();
        while (_connections.Reader.TryRead(out var leftover))
        {
            try { leftover.Socket.Dispose(); } catch { }
        }
    }
}
