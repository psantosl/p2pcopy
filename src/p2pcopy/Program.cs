using System.CommandLine;
using P2pCopy.App;
using P2pCopy.Net.Pairing;
using P2pCopy.Net.Stun;
using P2pCopy.Ui;
using Spectre.Console;

namespace P2pCopy;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var codeOption = new Option<string?>("--code", "-c") { Description = "The other side's pairing code. Skips the prompt.", Recursive = true };
        var portOption = new Option<int>("--port", "-p") { Description = "Local UDP/TCP port to use. Default: random.", Recursive = true };
        var stunOption = new Option<string[]>("--stun") { Description = "STUN server(s) as host[:port]. Repeat to add more.", Recursive = true };
        var timeoutOption = new Option<int>("--timeout") { Description = "Seconds to keep trying to reach the peer.", DefaultValueFactory = _ => 600, Recursive = true };
        var plainOption = new Option<bool>("--plain") { Description = "Plain text output, no colours or live progress.", Recursive = true };
        var jsonOption = new Option<bool>("--json") { Description = "Machine readable output: one JSON object per line.", Recursive = true };
        var verboseOption = new Option<bool>("--verbose", "-v") { Description = "Show candidates, probes and transport details.", Recursive = true };
        var noClipboardOption = new Option<bool>("--no-clipboard") { Description = "Do not copy the code to the clipboard.", Recursive = true };
        var noTcpOption = new Option<bool>("--no-tcp") { Description = "Disable the TCP route; use UDP hole punching only.", Recursive = true };
        var ipv4Option = new Option<bool>("--ipv4") { Description = "Do not use IPv6.", Recursive = true };

        var root = new RootCommand("Peer-to-peer file copy behind firewalls, with no server in between.\n" +
                                   "Both sides run p2pcopy, swap the code it prints, and the files flow directly.");
        root.Options.Add(codeOption);
        root.Options.Add(portOption);
        root.Options.Add(stunOption);
        root.Options.Add(timeoutOption);
        root.Options.Add(plainOption);
        root.Options.Add(jsonOption);
        root.Options.Add(verboseOption);
        root.Options.Add(noClipboardOption);
        root.Options.Add(noTcpOption);
        root.Options.Add(ipv4Option);

        var pathsArgument = new Argument<string[]>("paths") { Description = "Files and/or directories to send.", Arity = ArgumentArity.OneOrMore };
        var send = new Command("send", "Send files or directories to a peer.");
        send.Arguments.Add(pathsArgument);

        var outOption = new Option<string>("--out", "-o") { Description = "Directory to save into.", DefaultValueFactory = _ => "." };
        var yesOption = new Option<bool>("--yes", "-y") { Description = "Accept the incoming transfer without asking (overwrites existing files)." };
        var receive = new Command("receive", "Receive files from a peer.");
        receive.Options.Add(outOption);
        receive.Options.Add(yesOption);

        root.Subcommands.Add(send);
        root.Subcommands.Add(receive);

        SessionOptions Common(ParseResult pr, PeerRole role) => new()
        {
            Role = role,
            PeerCode = pr.GetValue(codeOption),
            Port = pr.GetValue(portOption),
            StunServers = ParseStun(pr.GetValue(stunOption)),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(5, pr.GetValue(timeoutOption))),
            Verbose = pr.GetValue(verboseOption),
            NoClipboard = pr.GetValue(noClipboardOption),
            NoTcp = pr.GetValue(noTcpOption),
            Ipv4Only = pr.GetValue(ipv4Option),
            Paths = role == PeerRole.Sender ? pr.GetValue(pathsArgument) ?? Array.Empty<string>() : Array.Empty<string>(),
            OutputDirectory = role == PeerRole.Receiver ? pr.GetValue(outOption) ?? "." : ".",
            Yes = role == PeerRole.Receiver && pr.GetValue(yesOption),
        };

        send.SetAction((pr, ct) => RunAsync(Common(pr, PeerRole.Sender), ChooseUi(pr.GetValue(plainOption), pr.GetValue(jsonOption)), ct));
        receive.SetAction((pr, ct) => RunAsync(Common(pr, PeerRole.Receiver), ChooseUi(pr.GetValue(plainOption), pr.GetValue(jsonOption)), ct));

        return await root.Parse(args).InvokeAsync();
    }

    private static IReadOnlyList<StunServer> ParseStun(string[]? values)
    {
        if (values == null || values.Length == 0)
        {
            var env = Environment.GetEnvironmentVariable("P2PCOPY_STUN");
            if (string.IsNullOrWhiteSpace(env)) return StunServer.Defaults;
            values = env.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
        return values.Select(StunServer.Parse).ToList();
    }

    private static IUserInterface ChooseUi(bool plain, bool json)
    {
        if (json) return new JsonUi();
        if (plain || Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.Interactive || Environment.GetEnvironmentVariable("TERM") == "dumb")
            return new PlainUi();
        return new SpectreUi();
    }

    private static async Task<int> RunAsync(SessionOptions options, IUserInterface ui, CancellationToken ct)
    {
        try
        {
            return await new TransferSession(options, ui).RunAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            ui.Error("Unexpected error: " + ex.Message);
            if (options.Verbose) ui.Detail(ex.ToString());
            return ExitCodes.Transfer;
        }
    }
}
