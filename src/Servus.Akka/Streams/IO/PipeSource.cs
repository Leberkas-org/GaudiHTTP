using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;

namespace Servus.Akka.Streams.IO;

public class PipeSource
{
    public static Source<ReadOnlyMemory<byte>, NotUsed> From(PipeReader reader)
    {
        return Source.FromGraph(new PipeSourceStage(reader));
    }
}