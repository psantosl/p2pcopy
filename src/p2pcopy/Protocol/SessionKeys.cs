using System.Security.Cryptography;
using P2pCopy.Net.Pairing;
using P2pCopy.Net.Transport;

namespace P2pCopy.Protocol;

internal enum AeadKind : byte
{
    AesGcm = 1,
    ChaCha20Poly1305 = 2,
}

internal sealed class SessionKeyException : Exception
{
    public SessionKeyException(string message) : base(message) { }
}

internal static class SessionKeys
{
    public const int KeySize = 32;

    public static AeadKind Negotiate(HelloFlags mine, HelloFlags theirs)
    {
        var both = mine & theirs;
        if (both.HasFlag(HelloFlags.SupportsAesGcm)) return AeadKind.AesGcm;
        if (both.HasFlag(HelloFlags.SupportsChaCha20Poly1305)) return AeadKind.ChaCha20Poly1305;
        throw new SessionKeyException("The two machines share no supported cipher (AES-GCM or ChaCha20-Poly1305).");
    }

    /// <summary>
    /// The session key comes from every pairing secret both sides know: the sender's if the
    /// receiver pasted the sender's code, the receiver's if the sender pasted the receiver's.
    /// Nonces from both HELLOs salt it so a replayed session never reuses a key.
    /// </summary>
    public static byte[] Derive(PeerConnection conn, PairingState pairing, PeerRole myRole)
    {
        var iKnowPeerSecret = pairing.Peer != null;
        var peerKnowsMySecret = conn.PeerHello.Flags.HasFlag(HelloFlags.KnowsPeerSecret);

        byte[]? senderSecret;
        byte[]? receiverSecret;
        byte[] senderNonce;
        byte[] receiverNonce;

        if (myRole == PeerRole.Sender)
        {
            senderSecret = peerKnowsMySecret ? pairing.Own.Secret : null;
            receiverSecret = iKnowPeerSecret ? pairing.Peer!.Secret : null;
            senderNonce = conn.OwnHello.Nonce;
            receiverNonce = conn.PeerHello.Nonce;
        }
        else
        {
            receiverSecret = peerKnowsMySecret ? pairing.Own.Secret : null;
            senderSecret = iKnowPeerSecret ? pairing.Peer!.Secret : null;
            receiverNonce = conn.OwnHello.Nonce;
            senderNonce = conn.PeerHello.Nonce;
        }

        if (senderSecret == null && receiverSecret == null)
            throw new SessionKeyException("Neither side has the other's pairing code, so there is no shared secret to encrypt with.");

        var ikm = new byte[(senderSecret?.Length ?? 0) + (receiverSecret?.Length ?? 0)];
        var pos = 0;
        if (senderSecret != null) { senderSecret.CopyTo(ikm, pos); pos += senderSecret.Length; }
        if (receiverSecret != null) { receiverSecret.CopyTo(ikm, pos); }

        var salt = new byte[senderNonce.Length + receiverNonce.Length];
        senderNonce.CopyTo(salt, 0);
        receiverNonce.CopyTo(salt, senderNonce.Length);

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, KeySize, salt, "p2pcopy v2 session key"u8.ToArray());
    }
}
