using System.Net;
using TurboHTTP.Server;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Tests.Server.Routing;

public sealed class RouteTableSpec
{
    [Fact(Timeout = 5000)]
    public void Match_should_find_exact_static_route()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/health", _ => Task.FromResult(new HttpResponseMessage()))
            .Build();

        var result = table.Match("GET", "/api/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_return_no_match_for_unknown_path()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/health", _ => Task.FromResult(new HttpResponseMessage()))
            .Build();

        var result = table.Match("GET", "/api/unknown");
        Assert.False(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_extract_route_parameters()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/orders/{id}", _ => Task.FromResult(new HttpResponseMessage()))
            .Build();

        var result = table.Match("GET", "/api/orders/42");
        Assert.True(result.IsMatch);
        Assert.Equal("42", result.RouteValues["id"]);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_extract_multiple_parameters()
    {
        var table = new RouteTableBuilder()
            .Add("GET", "/api/{controller}/{id}", _ => Task.FromResult(new HttpResponseMessage()))
            .Build();

        var result = table.Match("GET", "/api/orders/42");
        Assert.True(result.IsMatch);
        Assert.Equal("orders", result.RouteValues["controller"]);
        Assert.Equal("42", result.RouteValues["id"]);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_respect_http_method()
    {
        var table = new RouteTableBuilder()
            .Add("POST", "/api/orders", _ => Task.FromResult(new HttpResponseMessage()))
            .Build();

        var result = table.Match("GET", "/api/orders");
        Assert.False(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_support_wildcard_method()
    {
        var table = new RouteTableBuilder()
            .Add("*", "/api/health", _ => Task.FromResult(new HttpResponseMessage()))
            .Build();

        Assert.True(table.Match("GET", "/api/health").IsMatch);
        Assert.True(table.Match("POST", "/api/health").IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void Match_should_prefer_static_over_parameterized()
    {
        Func<TurboHttpContext, Task<HttpResponseMessage>> staticHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        Func<TurboHttpContext, Task<HttpResponseMessage>> paramHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));

        var table = new RouteTableBuilder()
            .Add("GET", "/api/orders/latest", staticHandler)
            .Add("GET", "/api/orders/{id}", paramHandler)
            .Build();

        var result = table.Match("GET", "/api/orders/latest");
        Assert.True(result.IsMatch);
        Assert.Same(staticHandler, result.Handler);
    }
}
