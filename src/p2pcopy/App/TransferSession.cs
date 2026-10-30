using System.Net.Sockets;
using System.Reflection;
using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;
using P2pCopy.Net.Stun;
using P2pCopy.Net.Transport;
using P2pCopy.Net.Udp;
using P2pCopy.Protocol;
using P2pCopy.Ui;

namespace P2pCopy.App;

/// <summary>One run of p2pcopy, send or receive, from network probe to final summary.</summary>
internal sealed class TransferSession
{
    private readonly SessionOptions _opts;
    private readonly IUserInterface _ui;
    private readonly Action<string>? _log;

    public TransferSession(SessionOptions options, IUserInterface ui)
    {
        _opts = options;
        _ui = ui;
        _log = options.Verbose ? ui.Detail : null;
    }

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "2.0.0";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        _ui.Banner(Version);

        Manifest? manifest = null;
        if (_opts.Role == PeerRole.Sender)
        {
            try
            {
                manifest = TransferPlan.Build(_opts.Paths);
            }
            catch (TransferPlanException ex)
            {
                _ui.Error(ex.Message);
                return ExitCodes.Files;
            }
            _ui.Info($"Ready to send {manifest.FileCount} file(s), {Format.Bytes(manifest.TotalBytes)}" +
                     (manifest.DirectoryCount > 0 ? $" in {manifest.DirectoryCount} folder(s)" : ""));
        }
        else
        {
            var dest = Path.GetFullPath(_opts.OutputDirectory);
            if (File.Exists(dest))
            {
                _ui.Error($"'{dest}' is a file, not a directory.");
                return ExitCodes.Files;
            }
        }

        List<UdpHub> hubs;
        int port;
        try
        {
            (hubs, port) = BindHubs();
        }
        catch (SocketException ex)
        {
            _ui.Error($"Could not open UDP port {_opts.Port}: {ex.Message}");
            return ExitCodes.Connect;
        }

        try
        {
            return await RunWithHubsAsync(hubs, port, manifest, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ui.Warn("Cancelled.");
            return ExitCodes.Cancelled;
        }
        finally
        {
            foreach (var h in hubs) h.Dispose();
        }
    }

    private async Task<int> RunWithHubsAsync(List<UdpHub> hubs, int port, Manifest? manifest, CancellationToken ct)
    {
        var v4 = hubs.FirstOrDefault(h => h.Family == AddressFamily.InterNetwork);
        var v6 = hubs.FirstOrDefault(h => h.Family == AddressFamily.InterNetworkV6);
        foreach (var h in hubs) h.Start();

        var probe = await _ui.StatusAsync("Looking at the network…", set =>
            NetworkProbe.RunAsync(v4, v6, _opts.StunServers, TimeSpan.FromSeconds(2.5), set, ct)).ConfigureAwait(false);

        _ui.ShowNetwork(probe, port);
        if (probe.Candidates.Count == 0)
        {
            _ui.Error("No usable network address was found. Is the machine online?");
            return ExitCodes.Connect;
        }
        if (probe.Nat == NatKind.UdpBlocked)
            _ui.Warn("No STUN server answered. Outbound UDP may be blocked here; only LAN or IPv6 routes can work.");

        var own = new PairingCode(_opts.Role, probe.Nat, PairingCode.NewSecret(), probe.Candidates);
        var encoded = own.Encode();
        var copied = !_opts.NoClipboard && Clipboard.TryCopy(encoded);
        var pairing = new PairingState(own);
        _ui.ShowOwnCode(own, encoded, copied, peerOnlyNeedsThis: probe.LikelyReachable);

        using var racer = new ConnectionRacer(hubs, port, pairing, _opts.Role, enableTcp: !_opts.NoTcp, _log);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var connectTask = racer.RunAsync(_opts.ConnectTimeout, connectCts.Token);
        var keepalive = v4 == null ? Task.CompletedTask : KeepAliveAsync(v4, racer, connectTask, ct);

        if (_opts.PeerCode != null)
        {
            if (!TryAcceptPeerCode(_opts.PeerCode, pairing, out var error))
            {
                _ui.Error(error!);
                connectCts.Cancel();
                return ExitCodes.Usage;
            }
        }
        else
        {
            await PromptForPeerCodeAsync(pairing, connectTask, probe, ct).ConfigureAwait(false);
        }

        var hint = pairing.Peer == null ? Task.CompletedTask : SecondCodeHintAsync(pairing.Peer, encoded, connectTask, ct);

        PeerConnection conn;
        try
        {
            conn = await _ui.StatusAsync("Connecting…", async set =>
            {
                var started = DateTime.UtcNow;
                while (!connectTask.IsCompleted)
                {
                    var n = pairing.Peer?.Candidates.Count ?? 0;
                    var what = n > 0 ? $"trying {n} route(s) to your peer" : "waiting for your peer to reach us";
                    set($"Connecting… {what}  {Format.Duration(DateTime.UtcNow - started)}");
                    await Task.WhenAny(connectTask, Task.Delay(1000, ct)).ConfigureAwait(false);
                }
                return await connectTask.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (PeerRoleConflictException ex)
        {
            _ui.Error(ex.Message);
            return ExitCodes.Connect;
        }
        catch (ConnectTimeoutException)
        {
            _ui.ShowConnectFailure(probe, pairing.Peer);
            return ExitCodes.Connect;
        }

        using (conn)
        {
            byte[] key;
            AeadKind aead;
            try
            {
                aead = SessionKeys.Negotiate(conn.OwnHello.Flags, conn.PeerHello.Flags);
                key = SessionKeys.Derive(conn, pairing, _opts.Role);
            }
            catch (SessionKeyException ex)
            {
                _ui.Error(ex.Message);
                return ExitCodes.Connect;
            }

            using var channel = new SecureChannel(conn.Stream, key, aead, _opts.Role);
            _ui.ShowConnected(conn.Transport, conn.Remote.ToString(), conn.PeerHello.Name, channel.CipherName);

            try
            {
                return _opts.Role == PeerRole.Sender
                    ? await SendAsync(channel, manifest!, ct).ConfigureAwait(false)
                    : await ReceiveAsync(channel, ct).ConfigureAwait(false);
            }
            catch (PairingMismatchException ex)
            {
                _ui.Error(ex.Message);
                return ExitCodes.Connect;
            }
            catch (TransferRejectedException ex)
            {
                _ui.Warn("Transfer not accepted: " + ex.Message);
                return ExitCodes.Transfer;
            }
            catch (Exception ex) when (ex is IOException or ProtocolViolationException or EndOfStreamException or SocketException or UnauthorizedAccessException)
            {
                _ui.Error("Transfer failed: " + ex.Message);
                return ExitCodes.Transfer;
            }
        }
    }

    private async Task<int> SendAsync(SecureChannel channel, Manifest manifest, CancellationToken ct)
    {
        var summary = await _ui.TransferAsync(manifest, PeerRole.Sender, progress =>
            Task.Run(() => FileSender.Run(channel, manifest, progress, ct), ct)).ConfigureAwait(false);
        _ui.ShowSummary(summary, PeerRole.Sender, "");
        return summary.Verified ? ExitCodes.Ok : ExitCodes.Transfer;
    }

    private async Task<int> ReceiveAsync(SecureChannel channel, CancellationToken ct)
    {
        var destination = Path.GetFullPath(_opts.OutputDirectory);
        var incoming = await Task.Run(() => FileReceiver.ReadManifest(channel, destination), ct).ConfigureAwait(false);

        if (incoming.RejectedPaths.Count > 0)
        {
            FileReceiver.Reject(channel, "unsafe paths: " + string.Join(", ", incoming.RejectedPaths.Take(3)));
            _ui.Error("Refused: the sender's file names are not safe to write here (" + string.Join(", ", incoming.RejectedPaths.Take(3)) + ").");
            return ExitCodes.Transfer;
        }

        _ui.ShowIncoming(incoming, destination);
        if (!_opts.Yes)
        {
            var question = incoming.AlreadyExisting.Count > 0 ? "Accept and overwrite existing files?" : "Accept?";
            var ok = await _ui.ConfirmAsync(question, defaultYes: incoming.AlreadyExisting.Count == 0, ct).ConfigureAwait(false);
            if (!ok)
            {
                FileReceiver.Reject(channel, "declined by the receiver");
                _ui.Warn("Declined.");
                return ExitCodes.Transfer;
            }
        }

        var summary = await _ui.TransferAsync(incoming.Manifest, PeerRole.Receiver, progress =>
            Task.Run(() => FileReceiver.Run(channel, incoming, destination, progress, ct), ct)).ConfigureAwait(false);
        _ui.ShowSummary(summary, PeerRole.Receiver, destination);
        return summary.Verified ? ExitCodes.Ok : ExitCodes.Transfer;
    }

    private (List<UdpHub> Hubs, int Port) BindHubs()
    {
        var attempts = _opts.Port != 0 ? 1 : 20;
        SocketException? last = null;
        for (var i = 0; i < attempts; i++)
        {
            var port = _opts.Port != 0 ? _opts.Port : Random.Shared.Next(20000, 60000);
            UdpHub? v4 = null;
            UdpHub? v6 = null;
            try
            {
                v4 = UdpHub.Bind(AddressFamily.InterNetwork, port);
                if (!_opts.Ipv4Only && Socket.OSSupportsIPv6)
                {
                    try { v6 = UdpHub.Bind(AddressFamily.InterNetworkV6, port); }
                    catch (SocketException ex) { _log?.Invoke("no IPv6 socket: " + ex.SocketErrorCode); }
                }
                var hubs = new List<UdpHub> { v4 };
                if (v6 != null) hubs.Add(v6);
                _log?.Invoke($"listening on UDP port {port}" + (v6 != null ? " (IPv4 + IPv6)" : " (IPv4)"));
                return (hubs, port);
            }
            catch (SocketException ex)
            {
                last = ex;
                v4?.Dispose();
                v6?.Dispose();
            }
        }
        throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    private bool TryAcceptPeerCode(string text, PairingState pairing, out string? error)
    {
        if (!PairingCode.TryParse(text, out var peer, out error))
        {
            error = "That does not look like a p2pcopy code: " + error + ".";
            return false;
        }
        if (peer!.Role == _opts.Role)
        {
            var what = _opts.Role == PeerRole.Sender ? "sender" : "receiver";
            error = $"That code belongs to another {what}. One side must run 'p2pcopy send' and the other 'p2pcopy receive'.";
            return false;
        }
        if (peer.Secret.AsSpan().SequenceEqual(pairing.Own.Secret))
        {
            error = "That is your own code. You need the one your peer's screen shows.";
            return false;
        }
        pairing.SetPeer(peer);
        _log?.Invoke($"peer is {(peer.Role == PeerRole.Sender ? "sender" : "receiver")}, {peer.Nat.Describe()}, {peer.Candidates.Count} candidate(s): {string.Join(", ", peer.Candidates)}");
        return true;
    }

    private async Task PromptForPeerCodeAsync(PairingState pairing, Task<PeerConnection> connectTask, ProbeResult probe, CancellationToken ct)
    {
        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = connectTask.ContinueWith(_ => promptCts.Cancel(), TaskScheduler.Default);

        var prompt = probe.LikelyReachable
            ? "Peer's code (or just wait if they only needed yours):"
            : "Peer's code:";

        while (pairing.Peer == null && !connectTask.IsCompleted)
        {
            var line = await _ui.PromptPeerCodeAsync(prompt, promptCts.Token).ConfigureAwait(false);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!TryAcceptPeerCode(line, pairing, out var error))
                _ui.Error(error!);
        }
    }

    private async Task SecondCodeHintAsync(PairingCode peer, string ownEncoded, Task<PeerConnection> connectTask, CancellationToken ct)
    {
        try
        {
            if (peer.Nat == NatKind.Symmetric)
            {
                _ui.ShowSecondCodeHint(ownEncoded, "Your peer's network is a symmetric NAT: they cannot be reached with their code alone.");
                return;
            }
            var wait = peer.LikelyReachable ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3);
            await Task.WhenAny(connectTask, Task.Delay(wait, ct)).ConfigureAwait(false);
            if (!connectTask.IsCompleted)
            {
                var reason = peer.LikelyReachable
                    ? "Still trying. If it does not connect soon, your peer probably needs your code too."
                    : $"Your peer's network ({peer.Nat.Describe()}) probably needs to reach out to us first.";
                _ui.ShowSecondCodeHint(ownEncoded, reason);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task KeepAliveAsync(UdpHub v4, ConnectionRacer racer, Task<PeerConnection> connectTask, CancellationToken ct)
    {
        var server = _opts.StunServers.FirstOrDefault();
        if (server == null) return;
        try
        {
            while (!connectTask.IsCompleted)
            {
                await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(20), ct)).ConfigureAwait(false);
                if (connectTask.IsCompleted || racer.ChosenHub != null) return;
                // Keeps our NAT mapping alive while the peer takes their time.
                await StunClient.QueryOneAsync(v4, server, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }
}
