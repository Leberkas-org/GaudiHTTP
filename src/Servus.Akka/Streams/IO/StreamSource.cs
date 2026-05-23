using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;

namespace Servus.Akka.Streams.IO;

public static class StreamSource
{
    public static Source<ReadOnlyMemory<byte>, NotUsed> From(PipeReader reader)
    {
        return Source.FromGraph(new PipeReaderSourceStage(reader));
    }

    public static Source<ReadOnlyMemory<byte>, NotUsed> From(Stream stream)
    {
        return From(PipeReader.Create(stream));
    }
}
