using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Server.Entity.Resolvers;

public sealed class ChildPerEntityResolver<TKey> : IEntityActorResolver<TKey>
{
    public sealed record GetOrCreateChild(string EntityKey);

    public async ValueTask<IActorRef> ResolveAsync(string entityKey, IServiceProvider services, CancellationToken ct)
    {
        var registry = services.GetRequiredService<ActorRegistry>();
        var parent = await registry.GetAsync<TKey>(ct);
        var child = await parent.Ask<IActorRef>(new GetOrCreateChild(entityKey), TimeSpan.FromSeconds(5), ct);
        return child;
    }
}
