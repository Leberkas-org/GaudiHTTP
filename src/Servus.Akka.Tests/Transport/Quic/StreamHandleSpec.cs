using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

[Collection("TransportBuffer")]
public sealed class StreamHandleSpec
{
    [Fact(Timeout = 5000)]
    public void WriteAsync_should_write_buffer_to_stream()
    {
        var ms = new MemoryStream();
        var handle = new StreamHandle(ms);

        var buffer = TransportBuffer.Rent(16);
        buffer.FullMemory.Span[0] = 0xAA;
        buffer.FullMemory.Span[1] = 0xBB;
        buffer.Length = 2;

        handle.Write(buffer);

        Assert.Equal(2, ms.Position);
        Assert.Equal(0xAA, ms.GetBuffer()[0]);
        Assert.Equal(0xBB, ms.GetBuffer()[1]);
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_read_from_stream()
    {
        var ms = new MemoryStream([0x01, 0x02, 0x03]);
        var handle = new StreamHandle(ms);

        var buf = new byte[16];
        var read = await handle.ReadAsync(buf, CancellationToken.None);

        Assert.Equal(3, read);
        Assert.Equal(0x01, buf[0]);
    }

    [Fact(Timeout = 5000)]
    public void CompleteWrites_should_not_throw()
    {
        var handle = new StreamHandle(Stream.Null);
        handle.CompleteWrites();
    }
}