using System.Text;
using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Features.Sse;

public static class SseFormatterFlow
{
    public static Flow<ServerSentEvent, ReadOnlyMemory<byte>, NotUsed> Instance { get; }
        = Flow.Create<ServerSentEvent>().Select(Format);

    internal static ReadOnlyMemory<byte> Format(ServerSentEvent evt)
    {
        var sb = new StringBuilder();

        if (evt.EventType is not null)
        {
            sb.Append("event: ");
            sb.Append(evt.EventType);
            sb.Append('\n');
        }

        if (evt.Id is not null)
        {
            sb.Append("id: ");
            sb.Append(evt.Id);
            sb.Append('\n');
        }

        if (evt.Retry is not null)
        {
            sb.Append("retry: ");
            sb.Append((long)evt.Retry.Value.TotalMilliseconds);
            sb.Append('\n');
        }

        var lines = evt.Data.Split('\n');
        foreach (var line in lines)
        {
            sb.Append("data: ");
            sb.Append(line);
            sb.Append('\n');
        }

        sb.Append('\n');

        return Encoding.UTF8.GetBytes(sb.ToString()).AsMemory();
    }
}
