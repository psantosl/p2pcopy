using System.Net;
using System.Net.Sockets;

namespace P2pCopy.Net.Udp;

/// <summary>
/// Owns one bound UDP socket and runs a receive loop that fans datagrams out to
/// subscribers. STUN, hole punching and keepalives all share this socket, and once a
/// peer is chosen the loop is stopped and the raw socket is handed to UDT.
/// </summary>
internal sealed class UdpHub : IDisposable
{
    private readonly CancellationTokenSource _loopCts = new();
    private readonly List<Func<IPEndPoint, ReadOnlyMemory<byte>, bool>> _handlers = new();
    private readonly object _gate = new();
    private Task? _loop;
    private bool _disposed;
    private bool _detached;

    public Socket Socket { get; }
    public AddressFamily Family => Socket.AddressFamily;
    public int LocalPort => ((IPEndPoint)Socket.LocalEndPoint!).Port;

    private UdpHub(Socket socket)
    {
        Socket = socket;
    }

    public static UdpHub Bind(AddressFamily family, int port)
    {
        var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (family == AddressFamily.InterNetworkV6)
                socket.DualMode = false;

            // Punching sends to addresses that may answer with ICMP unreachable. On Windows
            // that surfaces as a ConnectionReset on the *next* receive; turn that off.
            if (OperatingSystem.IsWindows())
            {
                const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
                try { socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); } catch { }
            }

            socket.ReceiveBufferSize = 4 * 1024 * 1024;
            socket.SendBufferSize = 1024 * 1024;

            var any = family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
            socket.Bind(new IPEndPoint(any, port));
            return new UdpHub(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Registers a handler. Return true from the handler to stop further dispatch.</summary>
    public IDisposable Subscribe(Func<IPEndPoint, ReadOnlyMemory<byte>, bool> handler)
    {
        lock (_gate) _handlers.Add(handler);
        return new Subscription(this, handler);
    }

    public void Start()
    {
        if (_loop != null) return;
        _loop = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>Stops the receive loop. The socket stays open and usable.</summary>
    public async Task StopAsync()
    {
        if (_loop == null) return;
        _loopCts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { }
        _loop = null;
    }

    /// <summary>Stops the loop and gives up ownership of the socket (for UDT).</summary>
    public async Task<Socket> DetachSocketAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _detached = true;
        return Socket;
    }

    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> data, IPEndPoint target, CancellationToken ct = default)
    {
        if (target.AddressFamily != Family)
            throw new ArgumentException("Address family mismatch", nameof(target));
        return Socket.SendToAsync(data, SocketFlags.None, target, ct);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var anyEp = new IPEndPoint(Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        var ct = _loopCts.Token;

        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await Socket.ReceiveFromAsync(buffer, SocketFlags.None, anyEp, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // ICMP errors and friends; keep listening.
                continue;
            }

            if (result.ReceivedBytes <= 0) continue;
            var from = (IPEndPoint)result.RemoteEndPoint;
            var datagram = new ReadOnlyMemory<byte>(buffer, 0, result.ReceivedBytes).ToArray();

            Func<IPEndPoint, ReadOnlyMemory<byte>, bool>[] handlers;
            lock (_gate) handlers = _handlers.ToArray();
            foreach (var h in handlers)
            {
                try
                {
                    if (h(from, datagram)) break;
                }
                catch
                {
                    // A misbehaving subscriber must not kill the loop.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loopCts.Cancel();
        if (!_detached)
        {
            try { Socket.Dispose(); } catch { }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly UdpHub _hub;
        private readonly Func<IPEndPoint, ReadOnlyMemory<byte>, bool> _handler;
        public Subscription(UdpHub hub, Func<IPEndPoint, ReadOnlyMemory<byte>, bool> handler) { _hub = hub; _handler = handler; }
        public void Dispose() { lock (_hub._gate) _hub._handlers.Remove(_handler); }
    }
}
