using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;
using P2pCopy.Protocol;
using Spectre.Console;

namespace P2pCopy.Ui;

/// <summary>The rich, interactive terminal experience.</summary>
internal sealed class SpectreUi : IUserInterface
{
    private static readonly Style Accent = new(Color.Aqua);
    private readonly IAnsiConsole _console;

    public SpectreUi(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    private static string E(string s) => Markup.Escape(s);

    public void Banner(string version)
    {
        _console.Write(new FigletText("p2pcopy").Color(Color.Aqua));
        _console.MarkupLine($"[grey]v{E(version)}  ·  peer-to-peer file copy, no server in between[/]");
        _console.WriteLine();
    }

    public void Info(string message) => _console.MarkupLine("[aqua]›[/] " + E(message));
    public void Detail(string message) => _console.MarkupLine("  [grey]" + E(message) + "[/]");
    public void Warn(string message) => _console.MarkupLine("[yellow]![/] " + E(message));
    public void Error(string message) => _console.MarkupLine("[red]✘[/] " + E(message));
    public void Success(string message) => _console.MarkupLine("[green]✔[/] " + E(message));

    public Task<T> StatusAsync<T>(string text, Func<Action<string>, Task<T>> work)
    {
        return _console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Accent)
            .StartAsync(E(text), ctx => work(s => ctx.Status(E(s))));
    }

    public void ShowNetwork(ProbeResult probe, int port)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn(new TableColumn("[grey]Candidate[/]"));
        table.AddColumn(new TableColumn("[grey]Address[/]"));
        table.AddColumn(new TableColumn("[grey]Note[/]"));
        foreach (var c in probe.Candidates)
        {
            var note = c.Kind switch
            {
                CandidateKind.Lan => "same network only",
                CandidateKind.Ipv6 => "direct, no NAT",
                CandidateKind.Reflexive => $"via STUN, {probe.Nat.Describe()}",
                _ => "",
            };
            table.AddRow(E(c.KindLabel), $"[white]{E(c.EndPoint.ToString())}[/]", $"[grey]{E(note)}[/]");
        }
        if (probe.Candidates.Count == 0)
            table.AddRow("[red]none[/]", "", "[grey]no usable network address found[/]");
        _console.Write(table);
    }

    public void ShowOwnCode(PairingCode code, string encoded, bool copied, bool peerOnlyNeedsThis)
    {
        var body = new Rows(
            new Markup($"[bold white]{E(encoded)}[/]"),
            new Markup(copied ? "[grey]copied to your clipboard[/]" : "[grey]select and copy it[/]"));
        var panel = new Panel(body)
            .Header(" [aqua]Your code[/] ", Justify.Left)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Aqua)
            .Padding(1, 0, 1, 0);
        _console.Write(panel);
        if (peerOnlyNeedsThis)
            _console.MarkupLine("[grey]Send it to your peer. You look directly reachable, so they probably only need this.[/]");
        else if (code.Nat == NatKind.Cone)
            _console.MarkupLine("[grey]Send it to your peer and paste theirs below. Your router keeps a stable port mapping, so hole punching should work once both codes are exchanged.[/]");
        else if (code.Nat == NatKind.Symmetric)
            _console.MarkupLine("[grey]Send it to your peer and paste theirs below. Your router is a symmetric NAT: connecting will depend on your peer's side being friendlier.[/]");
        else
            _console.MarkupLine("[grey]Send it to your peer and paste theirs below.[/]");
        _console.WriteLine();
    }

    public void ShowSecondCodeHint(string encoded, string reason)
    {
        _console.WriteLine();
        _console.MarkupLine($"[yellow]![/] {E(reason)}");
        _console.MarkupLine($"  Ask your peer to paste this code on their side:  [bold white]{E(encoded)}[/]");
        _console.WriteLine();
    }

    public void ShowConnected(string transport, string remote, string peerName, string cipher)
    {
        _console.MarkupLine($"[green]✔[/] Connected to [bold]{E(peerName)}[/] at {E(remote)} via {E(transport)}  [grey]{E(cipher)}[/]");
    }

    public void ShowIncoming(IncomingTransfer incoming, string destination)
    {
        var m = incoming.Manifest;
        var tree = new Tree($"[aqua]{E(destination)}[/]").Guide(TreeGuide.Line);
        var shown = 0;
        foreach (var e in m.Entries)
        {
            if (shown++ >= 12)
            {
                tree.AddNode($"[grey]… and {m.Entries.Count - 12} more[/]");
                break;
            }
            var label = e.Kind == EntryKind.Directory
                ? $"[blue]{E(e.RelativePath)}/[/]"
                : $"{E(e.RelativePath)} [grey]{E(Format.Bytes(e.Size))}[/]";
            tree.AddNode(label);
        }
        _console.Write(new Panel(tree)
            .Header($" [aqua]Incoming[/] {m.FileCount} file(s), {E(Format.Bytes(m.TotalBytes))} ", Justify.Left)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Padding(1, 0, 1, 0));
        if (incoming.AlreadyExisting.Count > 0)
            Warn($"{incoming.AlreadyExisting.Count} file(s) already exist and would be overwritten: {string.Join(", ", incoming.AlreadyExisting.Take(3))}{(incoming.AlreadyExisting.Count > 3 ? ", …" : "")}");
    }

    public void ShowSummary(TransferSummary summary, PeerRole role, string destination)
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Files[/]", summary.Files.ToString());
        grid.AddRow("[grey]Bytes[/]", E(Format.Bytes(summary.Bytes)));
        grid.AddRow("[grey]Time[/]", E(Format.Duration(summary.Elapsed)));
        grid.AddRow("[grey]Speed[/]", E(Format.Speed(summary.BytesPerSecond)));
        grid.AddRow("[grey]Integrity[/]", summary.Verified ? "[green]SHA-256 verified[/]" : "[red]not verified[/]");
        if (role == PeerRole.Receiver) grid.AddRow("[grey]Saved to[/]", E(destination));
        _console.Write(new Panel(grid)
            .Header(role == PeerRole.Sender ? " [green]Sent[/] " : " [green]Received[/] ", Justify.Left)
            .Border(BoxBorder.Rounded)
            .BorderColor(summary.Verified ? Color.Green : Color.Red)
            .Padding(1, 0, 1, 0));
    }

    public void ShowConnectFailure(ProbeResult own, PairingCode? peer)
    {
        _console.WriteLine();
        Error("Could not reach your peer.");
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Your side[/]", E(own.Nat.Describe()) + (own.HasIpv6 ? ", IPv6 available" : ""));
        if (peer != null)
            grid.AddRow("[grey]Peer side[/]", E(peer.Nat.Describe()) + (peer.HasIpv6 ? ", IPv6 available" : ""));
        _console.Write(grid);
        _console.WriteLine();
        foreach (var hint in Diagnostics.Hints(own, peer))
            _console.MarkupLine("  [aqua]•[/] " + E(hint));
    }

    public async Task<string?> PromptPeerCodeAsync(string prompt, CancellationToken ct)
    {
        _console.Markup($"[aqua]?[/] {E(prompt)} ");
        try
        {
            var line = await ConsoleInput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) _console.WriteLine();
            return line;
        }
        catch (OperationCanceledException)
        {
            _console.WriteLine();
            throw;
        }
    }

    public Task<bool> ConfirmAsync(string question, bool defaultYes, CancellationToken ct)
    {
        return Task.Run(() => _console.Confirm(E(question), defaultYes), ct);
    }

    public async Task<T> TransferAsync<T>(Manifest manifest, PeerRole role, Func<IProgress<TransferProgress>, Task<T>> work)
    {
        var columns = new ProgressColumn[]
        {
            new TaskDescriptionColumn { Alignment = Justify.Left },
            new ProgressBarColumn { CompletedStyle = Accent, FinishedStyle = new Style(Color.Green), RemainingStyle = new Style(Color.Grey23) },
            new PercentageColumn(),
            new DownloadedColumn(),
            new TransferSpeedColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn(Spinner.Known.Dots) { Style = Accent },
        };

        return await _console.Progress()
            .AutoClear(false)
            .HideCompleted(true)
            .Columns(columns)
            .StartAsync(async ctx =>
            {
                var verb = role == PeerRole.Sender ? "Sending" : "Receiving";
                var total = ctx.AddTask($"[bold]{verb} {manifest.FileCount} file(s)[/]", maxValue: Math.Max(1, manifest.TotalBytes));
                var reporter = new Reporter(ctx, total);
                try
                {
                    return await work(reporter).ConfigureAwait(false);
                }
                finally
                {
                    reporter.Finish();
                }
            }).ConfigureAwait(false);
    }

    private sealed class Reporter : IProgress<TransferProgress>
    {
        private readonly ProgressContext _ctx;
        private readonly ProgressTask _total;
        private readonly object _gate = new();
        private ProgressTask? _file;
        private int _fileIndex = -1;

        public Reporter(ProgressContext ctx, ProgressTask total)
        {
            _ctx = ctx;
            _total = total;
        }

        public void Report(TransferProgress p)
        {
            lock (_gate)
            {
                _total.Value = Math.Min(p.BytesDone, _total.MaxValue);
                if (p.FileIndex != _fileIndex)
                {
                    _file?.StopTask();
                    _fileIndex = p.FileIndex;
                    var name = p.CurrentFile;
                    if (name.Length > 40) name = "…" + name.Substring(name.Length - 39);
                    _file = _ctx.AddTask($"[grey]{p.FileIndex}/{p.FileCount}[/] {E(name)}", maxValue: Math.Max(1, p.CurrentFileSize));
                }
                if (_file != null)
                {
                    _file.Value = p.CurrentFileSize == 0 ? _file.MaxValue : Math.Min(p.CurrentFileBytes, _file.MaxValue);
                    if (p.CurrentFileDone)
                    {
                        _file.Value = _file.MaxValue;
                        _file.StopTask();
                    }
                }
            }
        }

        public void Finish()
        {
            lock (_gate)
            {
                _file?.StopTask();
                _total.StopTask();
            }
        }
    }
}
