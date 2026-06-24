using System.Collections;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

/// <summary>
/// A thin generic view over a pooled <see cref="TurboFeatureCollection"/> that adds
/// <see cref="IHostContextContainer{TContext}"/>. ASP.NET's <c>HostingApplication.CreateContext</c>
/// checks <c>features is IHostContextContainer&lt;Context&gt;</c> and, when present, reuses the cached
/// host context (and with it the <c>DefaultHttpContext</c> graph) instead of allocating a new context,
/// <c>DefaultHttpRequest</c>, <c>DefaultHttpResponse</c>, and feature-reference caches every request.
/// </summary>
/// <remarks>
/// All <see cref="IFeatureCollection"/> members delegate to the wrapped collection, including
/// <see cref="Revision"/>, so ASP.NET's <c>DefaultHttpContext.Initialize(features, revision)</c> sees a
/// fresh revision each request and drops its stale feature caches (Items, Query, etc.). The container is
/// created once per pooled collection by the bridge and reused for that collection's lifetime; the
/// <typeparamref name="TContext"/> slot is stored untyped on the collection so the non-generic collection
/// can carry it across requests.
/// </remarks>
internal sealed class HostContextContainer<TContext> : IFeatureCollection, IHostContextContainer<TContext>
    where TContext : notnull
{
    private readonly TurboFeatureCollection _inner;

    public HostContextContainer(TurboFeatureCollection inner)
    {
        _inner = inner;
    }

    public TContext? HostContext
    {
        get => _inner.HostContext is TContext ctx ? ctx : default;
        set => _inner.HostContext = value;
    }

    private IFeatureCollection Inner => _inner;

    public bool IsReadOnly => Inner.IsReadOnly;

    public int Revision => Inner.Revision;

    public object? this[Type key]
    {
        get => Inner[key];
        set => Inner[key] = value;
    }

    public TFeature? Get<TFeature>() => Inner.Get<TFeature>();

    public void Set<TFeature>(TFeature? instance) => Inner.Set(instance);

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => Inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Inner).GetEnumerator();
}
