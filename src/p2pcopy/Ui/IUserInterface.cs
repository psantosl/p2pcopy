using P2pCopy.Net.Candidates;
using P2pCopy.Net.Pairing;
using P2pCopy.Protocol;

namespace P2pCopy.Ui;

/// <summary>Everything the session says to or asks the person. Three flavours: rich, plain, JSON.</summary>
internal interface IUserInterface
{
    void Banner(string version);
    void Info(string message);
    void Detail(string message);
    void Warn(string message);
    void Error(string message);
    void Success(string message);

    Task<T> StatusAsync<T>(string text, Func<Action<string>, Task<T>> work);

    void ShowNetwork(ProbeResult probe, int port);
    void ShowOwnCode(PairingCode code, string encoded, bool copied, bool peerOnlyNeedsThis);
    void ShowSecondCodeHint(string encoded, string reason);
    void ShowConnected(string transport, string remote, string peerName, string cipher);
    void ShowIncoming(IncomingTransfer incoming, string destination);
    void ShowSummary(TransferSummary summary, PeerRole role, string destination);
    void ShowConnectFailure(ProbeResult own, PairingCode? peer);

    /// <summary>Returns null when cancelled (e.g. the peer connected on its own).</summary>
    Task<string?> PromptPeerCodeAsync(string prompt, CancellationToken ct);
    Task<bool> ConfirmAsync(string question, bool defaultYes, CancellationToken ct);

    Task<T> TransferAsync<T>(Manifest manifest, PeerRole role, Func<IProgress<TransferProgress>, Task<T>> work);
}
