using UdtSharp;

namespace P2pCopy.Net.Transport;

/// <summary>Stream over a connected UDT socket. A broken connection reads as end of stream.</summary>
internal sealed class UdtStream : Stream
{
    private readonly UdtSocket _socket;
    private bool _closed;

    public UdtStream(UdtSocket socket)
    {
        _socket = socket;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0) return 0;
        try
        {
            var n = _socket.Receive(buffer, offset, count);
            return n < 0 ? 0 : n;
        }
        catch (Exception ex) when (ex is UdtException || ex.InnerException is UdtException)
        {
            return 0;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int sent;
            try
            {
                sent = _socket.Send(buffer, offset, count);
            }
            catch (Exception ex)
            {
                throw new IOException("UDT connection lost: " + (ex.InnerException?.Message ?? ex.Message), ex);
            }
            if (sent <= 0) throw new IOException("UDT connection closed while sending");
            offset += sent;
            count -= sent;
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            try { _socket.Close(); } catch { }
        }
        base.Dispose(disposing);
    }
}
