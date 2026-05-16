namespace TurboHTTP.Server.Middleware;

public sealed class TurboMiddlewareRegistry
{
    private readonly List<Func<IServiceProvider, IServerBidiStage>> _factories = new();

    public void Add<T>() where T : IServerBidiStage, new()
    {
        _factories.Add(_ => new T());
    }

    public void Add(Func<IServiceProvider, IServerBidiStage> factory)
    {
        _factories.Add(factory);
    }

    internal IReadOnlyList<IServerBidiStage> Resolve(IServiceProvider services)
    {
        var stages = new List<IServerBidiStage>(_factories.Count);
        foreach (var factory in _factories)
        {
            stages.Add(factory(services));
        }
        return stages;
    }
}
