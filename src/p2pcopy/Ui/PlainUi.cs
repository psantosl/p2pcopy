using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;
using P2pCopy.Protocol;

namespace P2pCopy.Ui;

/// <summary>No colours, no cursor movement: for pipes, logs and dumb terminals.</summary>
internal sealed class PlainUi : IUserInterface
{
    private readonly TextWriter _out = Console.Out;

    public void Banner(string version) => _out.WriteLine($"p2pcopy v{version}");
    public void Info(string message) => _out.WriteLine(message);
    public void Detail(string message) => _out.WriteLine("  " + message);
    public void Warn(string message) => _out.WriteLine("warning: " + message);
    public void Error(string message) => Console.Error.WriteLine("error: " + message);
    public void Success(string message) => _out.WriteLine(message);

    public Task<T> StatusAsync<T>(string text, Func<Action<string>, Task<T>> work)
    {
        _out.WriteLine(text);
        var last = DateTime.UtcNow;
        return work(s =>
        {
            // Status text changes every second; a log does not need that.
            if ((DateTime.UtcNow - last).TotalSeconds < 15) return;
            last = DateTime.UtcNow;
            _out.WriteLine(s);
        });
    }

    public void ShowNetwork(ProbeResult probe, int port)
    {
        foreach (var c in probe.Candidates)
            _out.WriteLine($"  {c.KindLabel,-8} {c.EndPoint}");
        _out.WriteLine($"  NAT: {probe.Nat.Describe()}");
    }

    public void ShowOwnCode(PairingCode code, string encoded, bool copied, bool peerOnlyNeedsThis)
    {
        _out.WriteLine();
        _out.WriteLine("Your code: " + encoded);
        _out.WriteLine(peerOnlyNeedsThis
            ? "Send it to your peer. You look directly reachable, so they probably only need this."
            : "Send it to your peer and paste theirs when asked.");
        _out.WriteLine();
    }

    public void ShowSecondCodeHint(string encoded, string reason)
    {
        _out.WriteLine(reason);
        _out.WriteLine("Ask your peer to paste this code on their side: " + encoded);
    }

    public void ShowConnected(string transport, string remote, string peerName, string cipher)
        => _out.WriteLine($"Connected to {peerName} at {remote} via {transport} ({cipher})");

    public void ShowIncoming(IncomingTransfer incoming, string destination)
    {
        var m = incoming.Manifest;
        _out.WriteLine($"Incoming: {m.FileCount} file(s), {Format.Bytes(m.TotalBytes)} -> {destination}");
        foreach (var e in m.Entries.Take(20))
            _out.WriteLine(e.Kind == EntryKind.Directory ? $"  {e.RelativePath}/" : $"  {e.RelativePath} ({Format.Bytes(e.Size)})");
        if (m.Entries.Count > 20) _out.WriteLine($"  ... and {m.Entries.Count - 20} more");
        if (incoming.AlreadyExisting.Count > 0)
            Warn($"{incoming.AlreadyExisting.Count} file(s) already exist and would be overwritten");
    }

    public void ShowSummary(TransferSummary summary, PeerRole role, string destination)
    {
        _out.WriteLine($"{(role == PeerRole.Sender ? "Sent" : "Received")} {summary.Files} file(s), {Format.Bytes(summary.Bytes)} in {Format.Duration(summary.Elapsed)} ({Format.Speed(summary.BytesPerSecond)}), {(summary.Verified ? "SHA-256 verified" : "NOT verified")}");
        if (role == PeerRole.Receiver) _out.WriteLine("Saved to " + destination);
    }

    public void ShowConnectFailure(ProbeResult own, PairingCode? peer)
    {
        Error("Could not reach your peer.");
        _out.WriteLine($"  your side: {own.Nat.Describe()}{(own.HasIpv6 ? ", IPv6 available" : "")}");
        if (peer != null) _out.WriteLine($"  peer side: {peer.Nat.Describe()}{(peer.HasIpv6 ? ", IPv6 available" : "")}");
        foreach (var h in Diagnostics.Hints(own, peer)) _out.WriteLine("  - " + h);
    }

    public async Task<string?> PromptPeerCodeAsync(string prompt, CancellationToken ct)
    {
        _out.Write(prompt + " ");
        _out.Flush();
        return await ConsoleInput.ReadLineAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ConfirmAsync(string question, bool defaultYes, CancellationToken ct)
    {
        _out.Write($"{question} [{(defaultYes ? "Y/n" : "y/N")}] ");
        _out.Flush();
        var line = await ConsoleInput.ReadLineAsync(ct).ConfigureAwait(false);
        if (line == null) return false;
        line = line.Trim();
        if (line.Length == 0) return defaultYes;
        return line.StartsWith('y') || line.StartsWith('Y');
    }

    public async Task<T> TransferAsync<T>(Manifest manifest, PeerRole role, Func<IProgress<TransferProgress>, Task<T>> work)
    {
        var reporter = new Reporter(_out, manifest.TotalBytes);
        var result = await work(reporter).ConfigureAwait(false);
        reporter.Flush();
        return result;
    }

    private sealed class Reporter : IProgress<TransferProgress>
    {
        private readonly TextWriter _out;
        private readonly long _total;
        private readonly object _gate = new();
        private DateTime _last = DateTime.MinValue;
        private readonly DateTime _start = DateTime.UtcNow;
        private TransferProgress _latest;

        public Reporter(TextWriter writer, long total) { _out = writer; _total = total; }

        public void Report(TransferProgress p)
        {
            lock (_gate)
            {
                _latest = p;
                var now = DateTime.UtcNow;
                if ((now - _last).TotalSeconds < 2 && !p.CurrentFileDone) return;
                _last = now;
                Write(p, now);
            }
        }

        private void Write(TransferProgress p, DateTime now)
        {
            var pct = _total == 0 ? 100 : p.BytesDone * 100 / _total;
            var speed = p.BytesDone / Math.Max(0.001, (now - _start).TotalSeconds);
            _out.WriteLine($"{pct,3}%  {Format.Bytes(p.BytesDone)} / {Format.Bytes(_total)}  {Format.Speed(speed)}  eta {Format.Eta(p.BytesDone, _total, speed)}  [{p.FileIndex}/{p.FileCount}] {p.CurrentFile}");
        }

        public void Flush()
        {
            lock (_gate)
            {
                if (_latest.TotalBytes > 0 && _latest.BytesDone == _latest.TotalBytes) Write(_latest, DateTime.UtcNow);
            }
        }
    }
}
