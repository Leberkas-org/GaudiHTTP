using Akka;
using Akka.Streams;

namespace GaudiHTTP.Streams.Lifecycle;

internal sealed record ListenerHandle(
    UniqueKillSwitch AcceptSwitch,
    Task<Done> CompletionTask);
