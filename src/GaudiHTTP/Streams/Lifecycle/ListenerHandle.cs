using Akka;
using Akka.Streams;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed record ListenerHandle(
    UniqueKillSwitch AcceptSwitch,
    Task<Done> CompletionTask);
