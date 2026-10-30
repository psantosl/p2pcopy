using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using P2pCopy.Net.Pairing;
using P2pCopy.Protocol;
using Xunit;

namespace P2pCopy.Tests;

public class ProtocolTests
{
    private static (Stream A, Stream B) ConnectedPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        var server = listener.AcceptTcpClient();
        listener.Stop();
        return (client.GetStream(), server.GetStream());
    }

    [Fact]
    public void SecureChannel_RoundTrips_AndDetectsWrongKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var (a, b) = ConnectedPair();
        var kind = AesGcm.IsSupported ? AeadKind.AesGcm : AeadKind.ChaCha20Poly1305;
        using var sender = new SecureChannel(a, key, kind, PeerRole.Sender);
        using var receiver = new SecureChannel(b, key, kind, PeerRole.Receiver);

        var payload = new byte[SecureChannel.MaxPlaintext];
        RandomNumberGenerator.Fill(payload);
        sender.WriteMessage(payload);
        sender.WriteMessage(new byte[] { 1, 2, 3 });
        sender.Flush();

        using (var m1 = receiver.ReadMessage()!) Assert.True(m1.Span.SequenceEqual(payload));
        using (var m2 = receiver.ReadMessage()!) Assert.Equal(new byte[] { 1, 2, 3 }, m2.Span.ToArray());

        receiver.WriteMessage(new byte[] { 9 });
        receiver.Flush();
        using (var m3 = sender.ReadMessage()!) Assert.Equal(9, m3.Span[0]);

        var wrong = RandomNumberGenerator.GetBytes(32);
        var (c, d) = ConnectedPair();
        using var s2 = new SecureChannel(c, key, kind, PeerRole.Sender);
        using var r2 = new SecureChannel(d, wrong, kind, PeerRole.Receiver);
        s2.WriteMessage(new byte[] { 1 });
        s2.Flush();
        Assert.Throws<PairingMismatchException>(() => r2.ReadMessage());
    }

    [Fact]
    public void Manifest_RoundTrips()
    {
        var m = new Manifest
        {
            Entries = new[]
            {
                new ManifestEntry(EntryKind.Directory, "photos", 0, 1700000000),
                new ManifestEntry(EntryKind.File, "photos/ünïcode 名前.jpg", 123456789012, 1700000001),
            },
        };
        var bytes = Messages.EncodeManifest(m);
        Assert.Equal((byte)MessageType.Manifest, bytes[0]);
        var back = Messages.DecodeManifest(bytes.AsSpan(1));
        Assert.Equal(2, back.Entries.Count);
        Assert.Equal("photos/ünïcode 名前.jpg", back.Entries[1].RelativePath);
        Assert.Equal(123456789012, back.Entries[1].Size);
        Assert.Equal(123456789012, back.TotalBytes);
    }

    [Theory]
    [InlineData("a/b/c.txt", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("a/../../b", false)]
    [InlineData("/abs/path", false)]
    [InlineData("C:/windows", false)]
    [InlineData("ok/./fine.txt", true)]
    [InlineData("", false)]
    public void PathSanitizer_RejectsEscapes(string path, bool ok)
    {
        var local = PathSanitizer.ToLocalRelativePath(path, out _);
        if (!ok)
        {
            Assert.Null(local);
            return;
        }
        Assert.NotNull(local);
        var dest = Path.Combine(Path.GetTempPath(), "p2pcopy-tests-dest");
        var full = PathSanitizer.ResolveInside(dest, local!);
        Assert.NotNull(full);
        Assert.StartsWith(Path.GetFullPath(dest), full!);
    }

    [Fact]
    public async Task EndToEnd_SendsFilesAndDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "p2pcopy-e2e-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "src");
        var dst = Path.Combine(root, "dst");
        Directory.CreateDirectory(Path.Combine(src, "dir", "nested"));
        var big = new byte[3 * 1024 * 1024 + 17];
        RandomNumberGenerator.Fill(big);
        File.WriteAllBytes(Path.Combine(src, "big.bin"), big);
        File.WriteAllText(Path.Combine(src, "dir", "nested", "hello.txt"), "hello");
        File.WriteAllBytes(Path.Combine(src, "dir", "empty.bin"), Array.Empty<byte>());

        try
        {
            var manifest = TransferPlan.Build(new[] { Path.Combine(src, "big.bin"), Path.Combine(src, "dir") });
            Assert.Equal(3, manifest.FileCount);

            var key = RandomNumberGenerator.GetBytes(32);
            var kind = AesGcm.IsSupported ? AeadKind.AesGcm : AeadKind.ChaCha20Poly1305;
            var (a, b) = ConnectedPair();
            using var sendCh = new SecureChannel(a, key, kind, PeerRole.Sender);
            using var recvCh = new SecureChannel(b, key, kind, PeerRole.Receiver);

            var progress = new Progress<TransferProgress>();
            var sendTask = Task.Run(() => FileSender.Run(sendCh, manifest, progress, CancellationToken.None));
            var recvTask = Task.Run(() =>
            {
                var incoming = FileReceiver.ReadManifest(recvCh, dst);
                Assert.Empty(incoming.RejectedPaths);
                return FileReceiver.Run(recvCh, incoming, dst, progress, CancellationToken.None);
            });

            var sent = await sendTask;
            var received = await recvTask;
            Assert.True(sent.Verified);
            Assert.True(received.Verified);
            Assert.Equal(big.Length + 5, sent.Bytes);
            Assert.Equal(big, File.ReadAllBytes(Path.Combine(dst, "big.bin")));
            Assert.Equal("hello", File.ReadAllText(Path.Combine(dst, "dir", "nested", "hello.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "dir", "empty.bin")));
            Assert.Empty(Directory.GetFiles(dst, "*.p2pcopy-part", SearchOption.AllDirectories));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
