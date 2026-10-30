using UdtSharp;
using Xunit;

namespace P2pCopy.Tests;

/// <summary>Regression test for the send-buffer growth bug in the UdtSharp port.</summary>
public class UdtSndBufferTests
{
    [Fact]
    public void Growth_WhileRingIsWrapped_KeepsDataInOrder()
    {
        const int mss = 8;
        var buffer = new SndBuffer(4, mss);
        var expected = new List<byte>();
        var received = new List<byte>();
        byte next = 0;

        byte[] Chunk(int packets)
        {
            var b = new byte[packets * mss];
            for (var i = 0; i < b.Length; i++) b[i] = next++;
            expected.AddRange(b);
            return b;
        }

        void Drain(int packets)
        {
            for (var i = 0; i < packets; i++)
            {
                byte[]? data = null;
                uint msgno = 0;
                var len = buffer.readData(ref data, ref msgno);
                Assert.Equal(mss, len);
                received.AddRange(data!.Take(len));
            }
        }

        // Fill 3 of 4 blocks, send them, ack 2: now first=2, current=last=3 and the ring is about to wrap.
        buffer.addBuffer(Chunk(3), 0, 3 * mss);
        Drain(3);
        buffer.ackData(2);

        // Adding 3 more packets needs growth while first (2) is "ahead" of where the new blocks go.
        buffer.addBuffer(Chunk(3), 0, 3 * mss);
        Drain(3);
        buffer.ackData(4);

        // Keep going through a few more growth/wrap cycles.
        for (var round = 0; round < 6; round++)
        {
            var packets = 5 + round * 3;
            buffer.addBuffer(Chunk(packets), 0, packets * mss);
            Drain(packets);
            buffer.ackData(packets);
        }

        Assert.Equal(expected, received);
    }
}
