using Akka.Actor;

namespace TurboHTTP.Server.Entity;

public interface IEntityActorResolver
{
    ValueTask<IActorRef> ResolveAsync(string entityKey, IServiceProvider services, CancellationToken ct);
}

public interface IEntityActorResolver<TKey> : IEntityActorResolver;
