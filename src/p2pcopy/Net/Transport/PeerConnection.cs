using System.Net;

namespace P2pCopy.Net.Transport;

/// <summary>A negotiated connection to the other peer, ready for the encrypted protocol.</summary>
internal sealed class PeerConnection : IDisposable
{
    public required Stream Stream { get; init; }
    public required string Transport { get; init; }
    public required IPEndPoint Remote { get; init; }
    public required HelloMessage OwnHello { get; init; }
    public required HelloMessage PeerHello { get; init; }

    public void Dispose()
    {
        try { Stream.Dispose(); } catch { }
    }
}

internal sealed class PeerRoleConflictException : Exception
{
    public PeerRoleConflictException(string message) : base(message) { }
}

internal sealed class ConnectTimeoutException : Exception
{
    public ConnectTimeoutException(string message) : base(message) { }
}
