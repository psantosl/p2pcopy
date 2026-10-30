namespace P2pCopy.Net.Pairing;

/// <summary>
/// Our own code plus, once the user pastes it, the peer's. Shared by the puncher and the
/// handshake so both pick up the peer's secret the moment it becomes known.
/// </summary>
internal sealed class PairingState
{
    private readonly object _gate = new();
    private PairingCode? _peer;

    public PairingState(PairingCode own)
    {
        Own = own;
    }

    public PairingCode Own { get; }

    public PairingCode? Peer
    {
        get { lock (_gate) return _peer; }
    }

    public event Action<PairingCode>? PeerSet;

    public void SetPeer(PairingCode peer)
    {
        lock (_gate)
        {
            if (_peer != null) return;
            _peer = peer;
        }
        PeerSet?.Invoke(peer);
    }

    /// <summary>Secrets we can verify incoming probes against.</summary>
    public IReadOnlyList<byte[]> KnownSecrets
    {
        get
        {
            var peer = Peer;
            return peer == null ? new[] { Own.Secret } : new[] { Own.Secret, peer.Secret };
        }
    }

    /// <summary>
    /// The secret we tag our outgoing probes with: one the peer is guaranteed to know.
    /// If we pasted their code they know it (they made it); otherwise they must have ours.
    /// </summary>
    public byte[] TagSecret => Peer?.Secret ?? Own.Secret;
}
