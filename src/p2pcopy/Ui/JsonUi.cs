using System.Text.Json;
using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;
using P2pCopy.Protocol;

namespace P2pCopy.Ui;

/// <summary>One JSON object per line on stdout, prompts answered by lines on stdin. For scripts.</summary>
internal sealed class JsonUi : IUserInterface
{
    private readonly Stream _stdout = Console.OpenStandardOutput();
    private readonly object _gate = new();

    private void Emit(string eventName, Action<Utf8JsonWriter>? fields = null)
    {
        lock (_gate)
        {
            using var w = new Utf8JsonWriter(_stdout, new JsonWriterOptions { Indented = false, SkipValidation = false });
            w.WriteStartObject();
            w.WriteString("event", eventName);
            w.WriteString("time", DateTimeOffset.UtcNow.ToString("o"));
            fields?.Invoke(w);
            w.WriteEndObject();
            w.Flush();
            _stdout.WriteByte((byte)'\n');
            _stdout.Flush();
        }
    }

    public void Banner(string version) => Emit("start", w => w.WriteString("version", version));
    public void Info(string message) => Emit("info", w => w.WriteString("message", message));
    public void Detail(string message) => Emit("detail", w => w.WriteString("message", message));
    public void Warn(string message) => Emit("warning", w => w.WriteString("message", message));
    public void Error(string message) => Emit("error", w => w.WriteString("message", message));
    public void Success(string message) => Emit("success", w => w.WriteString("message", message));

    public Task<T> StatusAsync<T>(string text, Func<Action<string>, Task<T>> work)
    {
        Emit("status", w => w.WriteString("message", text));
        return work(s => Emit("status", w => w.WriteString("message", s)));
    }

    public void ShowNetwork(ProbeResult probe, int port) => Emit("network", w =>
    {
        w.WriteNumber("port", port);
        w.WriteString("nat", probe.Nat.ToString());
        w.WriteStartArray("candidates");
        foreach (var c in probe.Candidates)
        {
            w.WriteStartObject();
            w.WriteString("kind", c.Kind.ToString());
            w.WriteString("address", c.EndPoint.Address.ToString());
            w.WriteNumber("port", c.EndPoint.Port);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    public void ShowOwnCode(PairingCode code, string encoded, bool copied, bool peerOnlyNeedsThis) => Emit("code", w =>
    {
        w.WriteString("code", encoded);
        w.WriteBoolean("likely_reachable", peerOnlyNeedsThis);
    });

    public void ShowSecondCodeHint(string encoded, string reason) => Emit("second_code_needed", w =>
    {
        w.WriteString("code", encoded);
        w.WriteString("reason", reason);
    });

    public void ShowConnected(string transport, string remote, string peerName, string cipher) => Emit("connected", w =>
    {
        w.WriteString("transport", transport);
        w.WriteString("remote", remote);
        w.WriteString("peer", peerName);
        w.WriteString("cipher", cipher);
    });

    public void ShowIncoming(IncomingTransfer incoming, string destination) => Emit("incoming", w =>
    {
        w.WriteString("destination", destination);
        w.WriteNumber("files", incoming.Manifest.FileCount);
        w.WriteNumber("bytes", incoming.Manifest.TotalBytes);
        w.WriteNumber("existing", incoming.AlreadyExisting.Count);
        w.WriteStartArray("entries");
        foreach (var e in incoming.Manifest.Entries)
        {
            w.WriteStartObject();
            w.WriteString("kind", e.Kind == EntryKind.Directory ? "dir" : "file");
            w.WriteString("path", e.RelativePath);
            w.WriteNumber("size", e.Size);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    public void ShowSummary(TransferSummary summary, PeerRole role, string destination) => Emit("done", w =>
    {
        w.WriteString("role", role.ToString().ToLowerInvariant());
        w.WriteNumber("files", summary.Files);
        w.WriteNumber("bytes", summary.Bytes);
        w.WriteNumber("seconds", summary.Elapsed.TotalSeconds);
        w.WriteBoolean("verified", summary.Verified);
        if (role == PeerRole.Receiver) w.WriteString("destination", destination);
    });

    public void ShowConnectFailure(ProbeResult own, PairingCode? peer) => Emit("connect_failed", w =>
    {
        w.WriteString("own_nat", own.Nat.ToString());
        if (peer != null) w.WriteString("peer_nat", peer.Nat.ToString());
        w.WriteStartArray("hints");
        foreach (var h in Diagnostics.Hints(own, peer)) w.WriteStringValue(h);
        w.WriteEndArray();
    });

    public async Task<string?> PromptPeerCodeAsync(string prompt, CancellationToken ct)
    {
        Emit("prompt", w => { w.WriteString("field", "peer_code"); w.WriteString("message", prompt); });
        return await ConsoleInput.ReadLineAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ConfirmAsync(string question, bool defaultYes, CancellationToken ct)
    {
        Emit("prompt", w => { w.WriteString("field", "confirm"); w.WriteString("message", question); w.WriteBoolean("default", defaultYes); });
        var line = await ConsoleInput.ReadLineAsync(ct).ConfigureAwait(false);
        if (line == null) return false;
        line = line.Trim().ToLowerInvariant();
        if (line.Length == 0) return defaultYes;
        return line is "y" or "yes" or "true" or "1";
    }

    public async Task<T> TransferAsync<T>(Manifest manifest, PeerRole role, Func<IProgress<TransferProgress>, Task<T>> work)
    {
        var last = DateTime.MinValue;
        var reporter = new Progress(p =>
        {
            var now = DateTime.UtcNow;
            if ((now - last).TotalMilliseconds < 500 && !p.CurrentFileDone && p.BytesDone != p.TotalBytes) return;
            last = now;
            Emit("progress", w =>
            {
                w.WriteNumber("bytes", p.BytesDone);
                w.WriteNumber("total", p.TotalBytes);
                w.WriteNumber("file_index", p.FileIndex);
                w.WriteNumber("file_count", p.FileCount);
                w.WriteString("file", p.CurrentFile);
                w.WriteNumber("file_bytes", p.CurrentFileBytes);
                w.WriteNumber("file_size", p.CurrentFileSize);
                w.WriteBoolean("file_done", p.CurrentFileDone);
            });
        });
        return await work(reporter).ConfigureAwait(false);
    }

    private sealed class Progress : IProgress<TransferProgress>
    {
        private readonly Action<TransferProgress> _onReport;
        private readonly object _gate = new();
        public Progress(Action<TransferProgress> onReport) { _onReport = onReport; }
        public void Report(TransferProgress value) { lock (_gate) _onReport(value); }
    }
}
