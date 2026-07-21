using System.Buffers;

namespace GaudiHTTP.Tests.Protocol.Syntax;

internal static class SequenceHelper
{
    public static ReadOnlySequence<byte> CreateMultiSegment(params byte[][] segments)
    {
        if (segments.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        if (segments.Length == 1)
        {
            return new ReadOnlySequence<byte>(segments[0]);
        }

        var first = new Segment(segments[0]);
        var current = first;

        for (var i = 1; i < segments.Length; i++)
        {
            current = current.Append(segments[i]);
        }

        return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}
