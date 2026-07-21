using System.Buffers;
using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol;

internal readonly struct TransportBufferWriter(IConnectionTransport transport) : IBufferWriter<byte>
{
    public void Advance(int count) => transport.Advance(count);
    public Memory<byte> GetMemory(int sizeHint = 0) => transport.GetMemory(sizeHint);
    public Span<byte> GetSpan(int sizeHint = 0) => transport.GetMemory(sizeHint).Span;
}
