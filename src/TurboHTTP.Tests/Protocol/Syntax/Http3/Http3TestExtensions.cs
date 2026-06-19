using System.Buffers;
using TurboHTTP.Protocol;

namespace TurboHTTP.Protocol.Syntax.Http3
{
    internal static class Http3TestExtensions
    {
        public static byte[] Serialize(this Http3Frame frame)
        {
            var buf = new byte[frame.SerializedSize];
            var span = buf.AsSpan();
            frame.WriteTo(ref span);
            return buf;
        }

        public static byte[] Serialize(this Settings settings)
        {
            var size = 0;
            foreach (var (id, val) in settings.AllParameters)
            {
                size += QuicVarInt.EncodedLength(id) + QuicVarInt.EncodedLength(val);
            }

            var buf = new byte[size];
            var span = buf.AsSpan();

            foreach (var (id, val) in settings.AllParameters)
            {
                var written = QuicVarInt.Encode(id, span);
                span = span[written..];
                written = QuicVarInt.Encode(val, span);
                span = span[written..];
            }

            return buf;
        }
    }
}

namespace TurboHTTP.Protocol.Syntax.Http3.Qpack
{
    internal static class QpackTestExtensions
    {
        public static ReadOnlyMemory<byte> Encode(this QpackEncoder encoder,
            IReadOnlyList<(string Name, string Value)> headers)
        {
            using var owner = MemoryPool<byte>.Shared.Rent(8 * 1024);
            var writer = SpanWriter.Create(owner.Memory.Span);
            var n = encoder.Encode(headers, ref writer);
            return owner.Memory[..n].ToArray();
        }
    }
}
