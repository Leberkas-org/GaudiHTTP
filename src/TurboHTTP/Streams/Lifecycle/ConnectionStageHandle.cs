using Akka;
using Akka.Streams;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed record ConnectionStageHandle(
    UniqueKillSwitch AcceptSwitch,
    SharedKillSwitch DrainSwitch,
    Task<Done> CompletionTask);
