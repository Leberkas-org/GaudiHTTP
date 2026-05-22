using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Context.Features;

public interface ITurboRequestBodyFeature
{
    Stream Body { get; }
    Source<ReadOnlyMemory<byte>, NotUsed> BodySource { get; }
}
