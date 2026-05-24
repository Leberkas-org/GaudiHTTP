using System.Buffers;
using System.Text;
using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Features.Sse;

internal static class SseFormatterFlow
{
    private static readonly byte[] EventPrefix = "event: "u8.ToArray();
    private static readonly byte[] IdPrefix = "id: "u8.ToArray();
    private static readonly byte[] RetryPrefix = "retry: "u8.ToArray();
    private static readonly byte[] DataPrefix = "data: "u8.ToArray();
    private static readonly byte[] Lf = [(byte)'\n'];

    public static Flow<ServerSentEvent, ReadOnlyMemory<byte>, NotUsed> Instance { get; }
        = Flow.Create<ServerSentEvent>().Select(Format);

    private static ReadOnlyMemory<byte> Format(ServerSentEvent evt)
    {
        var size = EstimateSize(evt);
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        var pos = 0;

        if (evt.EventType is not null && evt.EventType != "message")
        {
            pos += Write(buffer.AsSpan(pos), EventPrefix, evt.EventType);
        }

        if (evt.Id is not null && !evt.Id.Contains('\0'))
        {
            pos += Write(buffer.AsSpan(pos), IdPrefix, evt.Id);
        }

        if (evt.Retry is not null)
        {
            var ms = (long)evt.Retry.Value.TotalMilliseconds;
            if (ms >= 0)
            {
                pos += Write(buffer.AsSpan(pos), RetryPrefix, ms.ToString());
            }
        }

        var data = evt.Data.AsSpan();
        while (data.Length > 0)
        {
            var nlIndex = data.IndexOf('\n');
            var line = nlIndex >= 0 ? data[..nlIndex] : data;

            DataPrefix.CopyTo(buffer.AsSpan(pos));
            pos += DataPrefix.Length;
            pos += Encoding.UTF8.GetBytes(line, buffer.AsSpan(pos));
            buffer[pos++] = (byte)'\n';

            data = nlIndex >= 0 ? data[(nlIndex + 1)..] : default;
        }

        buffer[pos++] = (byte)'\n';

        var result = new byte[pos];
        buffer.AsSpan(0, pos).CopyTo(result);
        ArrayPool<byte>.Shared.Return(buffer);

        return result.AsMemory();
    }

    private static int Write(Span<byte> dest, byte[] prefix, string value)
    {
        prefix.CopyTo(dest);
        var written = prefix.Length;
        written += Encoding.UTF8.GetBytes(value, dest[written..]);
        dest[written++] = (byte)'\n';
        return written;
    }

    private static int EstimateSize(ServerSentEvent evt)
    {
        var size = evt.Data.Length * 2 + 32;
        if (evt.EventType is not null) size += evt.EventType.Length + 10;
        if (evt.Id is not null) size += evt.Id.Length + 6;
        if (evt.Retry is not null) size += 24;
        return size;
    }
}
