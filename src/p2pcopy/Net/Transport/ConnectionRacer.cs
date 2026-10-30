using System.Net;
using System.Net.Sockets;
using P2pCopy.Net.Pairing;
using P2pCopy.Net.Punch;
using P2pCopy.Net.Udp;
using UdtSharp;

namespace P2pCopy.Net.Transport;

/// <summary>
/// Runs every way of reaching the peer at once (UDP hole punch + UDT, TCP listen, TCP
/// simultaneous open) and performs the clear-text HELLO on each connection that comes up.
/// The sender picks the first negotiated connection and tells the receiver with a GO byte.
/// </summary>
internal sealed class ConnectionRacer : IDisposable
{
    private const byte Go = 0x01;
    private const byte Abort = 0x00;

    private readonly IReadOnlyList<UdpHub> _hubs;
    private readonly PairingState _pairing;
    private readonly PeerRole _role;
    private readonly Action<string>? _log;
    private readonly Puncher _puncher;
    private readonly TcpRacer? _tcp;
    private readonly TaskCompletionSource<PeerConnection> _winner = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private bool _decided;

    public ConnectionRacer(IReadOnlyList<UdpHub> hubs, int port, PairingState pairing, PeerRole role, bool enableTcp, Action<string>? log)
    {
        _hubs = hubs;
        _pairing = pairing;
        _role = role;
        _log = log;
        _puncher = new Puncher(hubs, pairing, controlling: role == PeerRole.Sender, log);
        if (enableTcp)
        {
            _tcp = new TcpRacer(port,
                v4: hubs.Any(h => h.Family == AddressFamily.InterNetwork),
                v6: hubs.Any(h => h.Family == AddressFamily.InterNetworkV6), log);
        }
        pairing.PeerSet += OnPeerSet;
        if (pairing.Peer is { } peer) OnPeerSet(peer);
    }

    public UdpHub? ChosenHub { get; private set; }

    private void OnPeerSet(PairingCode peer)
    {
        var remotes = peer.Candidates.Select(c => c.EndPoint).ToList();
        _puncher.AddRemotes(remotes);
        _tcp?.AddRemotes(remotes);
    }

    public async Task<PeerConnection> RunAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var token = linked.Token;

        foreach (var hub in _hubs) hub.Start();
        _tcp?.Start();

        var udtPath = Task.Run(() => UdtPathAsync(timeout, token), token);
        var tcpPath = _tcp == null ? Task.CompletedTask : Task.Run(() => TcpPathAsync(token), token);

        var timeoutTask = Task.Delay(timeout, token);
        var finished = await Task.WhenAny(_winner.Task, timeoutTask).ConfigureAwait(false);

        _cts.Cancel();
        if (finished == _winner.Task)
            return await _winner.Task.ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        throw new ConnectTimeoutException("Could not reach the peer within the time limit.");
    }

    private async Task UdtPathAsync(TimeSpan timeout, CancellationToken ct)
    {
        PunchResult? punched;
        try
        {
            punched = await _puncher.RunAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        if (punched == null) return;

        _log?.Invoke($"hole punched to {punched.Remote}, starting UDT rendezvous");
        Socket socket;
        try
        {
            socket = await punched.Hub.DetachSocketAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Invoke("could not take over the UDP socket: " + ex.Message);
            return;
        }
        ChosenHub = punched.Hub;

        UdtSocket? udt = null;
        try
        {
            udt = new UdtSocket(socket.AddressFamily, SocketType.Stream);
            udt.Bind(socket);
            await Task.Run(() => udt.Connect(punched.Remote), ct).ConfigureAwait(false);
            _log?.Invoke("UDT connected");
            await NegotiateAsync(new UdtStream(udt), "UDT over UDP", punched.Remote, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { udt?.Close(); } catch { }
        }
        catch (Exception ex)
        {
            _log?.Invoke("UDT rendezvous failed: " + (ex.InnerException?.Message ?? ex.Message));
            try { udt?.Close(); } catch { }
        }
    }

    private async Task TcpPathAsync(CancellationToken ct)
    {
        if (_tcp == null) return;
        try
        {
            await foreach (var (socket, how) in _tcp.Connections.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var remote = (IPEndPoint)socket.RemoteEndPoint!;
                _ = NegotiateAsync(new NetworkStream(socket, ownsSocket: true), how, remote, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task NegotiateAsync(Stream stream, string transport, IPEndPoint remote, CancellationToken ct)
    {
        var own = HelloMessage.CreateOwn(_role, knowsPeerSecret: _pairing.Peer != null);
        using var step = CancellationTokenSource.CreateLinkedTokenSource(ct);
        step.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await stream.WriteAsync(own.ToBytes(), step.Token).ConfigureAwait(false);
            await stream.FlushAsync(step.Token).ConfigureAwait(false);

            var buf = new byte[HelloMessage.Size];
            await stream.ReadExactlyAsync(buf, step.Token).ConfigureAwait(false);
            if (!HelloMessage.TryParse(buf, out var peer, out var error))
            {
                _log?.Invoke($"{transport} {remote}: bad HELLO: {error}");
                stream.Dispose();
                return;
            }

            if (peer!.Role == _role)
            {
                var what = _role == PeerRole.Sender ? "sender" : "receiver";
                _winner.TrySetException(new PeerRoleConflictException(
                    $"Both peers are running as {what}. One side must run 'p2pcopy send' and the other 'p2pcopy receive'."));
                stream.Dispose();
                return;
            }

            if (_role == PeerRole.Sender)
            {
                bool iWin;
                lock (_gate)
                {
                    iWin = !_decided;
                    _decided = true;
                }
                var verdict = new[] { iWin ? Go : Abort };
                await stream.WriteAsync(verdict, step.Token).ConfigureAwait(false);
                await stream.FlushAsync(step.Token).ConfigureAwait(false);
                if (!iWin)
                {
                    _log?.Invoke($"{transport} {remote}: connected too, closing");
                    stream.Dispose();
                    return;
                }
                _log?.Invoke($"using {transport} to {remote}");
                _winner.TrySetResult(new PeerConnection { Stream = stream, Transport = transport, Remote = remote, OwnHello = own, PeerHello = peer });
            }
            else
            {
                var verdict = new byte[1];
                await stream.ReadExactlyAsync(verdict, step.Token).ConfigureAwait(false);
                if (verdict[0] != Go)
                {
                    _log?.Invoke($"{transport} {remote}: sender chose another route, closing");
                    stream.Dispose();
                    return;
                }
                _log?.Invoke($"using {transport} to {remote}");
                if (!_winner.TrySetResult(new PeerConnection { Stream = stream, Transport = transport, Remote = remote, OwnHello = own, PeerHello = peer }))
                    stream.Dispose();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or EndOfStreamException or SocketException or ObjectDisposedException)
        {
            if (!ct.IsCancellationRequested)
                _log?.Invoke($"{transport} {remote}: handshake failed: {ex.Message}");
            stream.Dispose();
        }
    }

    public void Dispose()
    {
        _pairing.PeerSet -= OnPeerSet;
        _cts.Cancel();
        _puncher.Dispose();
        _tcp?.Dispose();
    }
}
