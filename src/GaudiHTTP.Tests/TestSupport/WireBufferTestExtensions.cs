using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.TestSupport;

/// <summary>
/// Test-side replacement for the deleted implicit <c>byte[] -&gt; TransportBuffer</c> conversion:
/// rents a <see cref="WireBuffer"/> from the shared pool, copies the bytes in, and sets
/// <see cref="WireBuffer.Length"/> to the source length so decoders see the fed byte count.
/// </summary>
public static class WireBufferTestExtensions
{
    public static WireBuffer ToWireBuffer(this byte[] data)
    {
        var buffer = WireBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    public static WireBuffer ToWireBuffer(this ReadOnlySpan<byte> data)
    {
        var buffer = WireBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    public static WireBuffer ToWireBuffer(this Span<byte> data)
        => ((ReadOnlySpan<byte>)data).ToWireBuffer();
}
