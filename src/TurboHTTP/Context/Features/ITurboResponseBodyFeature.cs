using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

public interface ITurboResponseBodyFeature : IHttpResponseBodyFeature
{
    Sink<ReadOnlyMemory<byte>, Task> BodySink { get; }
}