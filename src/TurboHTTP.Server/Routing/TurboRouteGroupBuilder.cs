namespace TurboHTTP.Server.Routing;

public sealed class TurboRouteGroupBuilder
{
    private readonly string _prefix;
    private readonly TurboRouteTable _table;

    internal TurboRouteGroupBuilder(string prefix, TurboRouteTable table)
    {
        _prefix = prefix;
        _table = table;
    }

    public ITurboRouteBuilder MapGet(string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return _table.Add("GET", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapGet(string pattern, Delegate handler)
    {
        return _table.Add("GET", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapPost(string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return _table.Add("POST", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapPost(string pattern, Delegate handler)
    {
        return _table.Add("POST", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapPut(string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return _table.Add("PUT", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapPut(string pattern, Delegate handler)
    {
        return _table.Add("PUT", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapDelete(string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return _table.Add("DELETE", _prefix + pattern, handler);
    }

    public ITurboRouteBuilder MapDelete(string pattern, Delegate handler)
    {
        return _table.Add("DELETE", _prefix + pattern, handler);
    }

    public TurboRouteGroupBuilder MapGroup(string prefix)
    {
        return new TurboRouteGroupBuilder(_prefix + prefix, _table);
    }
}
