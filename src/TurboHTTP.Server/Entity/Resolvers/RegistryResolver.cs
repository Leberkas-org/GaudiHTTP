using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Server.Entity.Resolvers;

public sealed class RegistryResolver<TKey> : IEntityActorResolver<TKey>
{
    public ValueTask<IActorRef> ResolveAsync(string entityKey, IServiceProvider services, CancellationToken ct)
    {
        var registry = services.GetRequiredService<ActorRegistry>();
        return ValueTask.FromResult(registry.Get<TKey>());
    }
}
