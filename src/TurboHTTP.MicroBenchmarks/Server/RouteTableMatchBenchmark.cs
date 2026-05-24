using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Routing;

namespace TurboHTTP.MicroBenchmarks.Server;

[Config(typeof(MicroBenchmarkConfig))]
public sealed class RouteTableMatchBenchmark
{
    private RouteTable _staticTable10 = null!;
    private RouteTable _staticTable100 = null!;
    private RouteTable _parameterizedTable10 = null!;
    private RouteTable _parameterizedTable100 = null!;
    private RouteTable _mixedTable = null!;
    private RouteTable _noMatchTable = null!;

    private string _staticPath10 = null!;
    private string _staticPath100 = null!;
    private string _parameterizedPath10 = null!;
    private string _parameterizedPath100 = null!;
    private string _mixedStaticPath = null!;
    private string _mixedParameterizedPath = null!;
    private string _noMatchPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        var noOpDispatcher = new DelegateDispatcher(_ => Task.CompletedTask);

        // Static routes with 10 entries
        var staticBuilder10 = new RouteTableBuilder();
        for (int i = 0; i < 10; i++)
        {
            staticBuilder10.Add("GET", $"/api/endpoint{i}", noOpDispatcher);
        }
        _staticTable10 = staticBuilder10.Build();
        _staticPath10 = "/api/endpoint9";

        // Static routes with 100 entries
        var staticBuilder100 = new RouteTableBuilder();
        for (int i = 0; i < 100; i++)
        {
            staticBuilder100.Add("GET", $"/api/endpoint{i}", noOpDispatcher);
        }
        _staticTable100 = staticBuilder100.Build();
        _staticPath100 = "/api/endpoint99";

        // Parameterized routes with 10 entries
        var paramBuilder10 = new RouteTableBuilder();
        for (int i = 0; i < 10; i++)
        {
            paramBuilder10.Add("GET", $"/items/{i}/details", noOpDispatcher);
        }
        _parameterizedTable10 = paramBuilder10.Build();
        _parameterizedPath10 = "/items/5/details";

        // Parameterized routes with 100 entries
        var paramBuilder100 = new RouteTableBuilder();
        for (int i = 0; i < 100; i++)
        {
            paramBuilder100.Add("GET", $"/items/{i}/details", noOpDispatcher);
        }
        _parameterizedTable100 = paramBuilder100.Build();
        _parameterizedPath100 = "/items/50/details";

        // Mixed routes: 50 static + 50 parameterized
        var mixedBuilder = new RouteTableBuilder();
        for (int i = 0; i < 50; i++)
        {
            mixedBuilder.Add("GET", $"/static/path{i}", noOpDispatcher);
        }
        for (int i = 0; i < 50; i++)
        {
            mixedBuilder.Add("POST", $"/dynamic/{i}/data", noOpDispatcher);
        }
        _mixedTable = mixedBuilder.Build();
        _mixedStaticPath = "/static/path25";
        _mixedParameterizedPath = "/dynamic/25/data";

        // No-match table: 100 routes that won't match
        var noMatchBuilder = new RouteTableBuilder();
        for (int i = 0; i < 100; i++)
        {
            noMatchBuilder.Add("GET", $"/nomatch/endpoint{i}", noOpDispatcher);
        }
        _noMatchTable = noMatchBuilder.Build();
        _noMatchPath = "/completely/different/path";
    }

    [Benchmark(Baseline = true)]
    public bool StaticRoute_10Entries()
    {
        var result = _staticTable10.Match("GET", _staticPath10);
        return result.IsMatch;
    }

    [Benchmark]
    public bool StaticRoute_100Entries()
    {
        var result = _staticTable100.Match("GET", _staticPath100);
        return result.IsMatch;
    }

    [Benchmark]
    public bool ParameterizedRoute_10Entries()
    {
        var result = _parameterizedTable10.Match("GET", _parameterizedPath10);
        return result.IsMatch;
    }

    [Benchmark]
    public bool ParameterizedRoute_100Entries()
    {
        var result = _parameterizedTable100.Match("GET", _parameterizedPath100);
        return result.IsMatch;
    }

    [Benchmark]
    public bool MixedRoutes_StaticHit()
    {
        var result = _mixedTable.Match("GET", _mixedStaticPath);
        return result.IsMatch;
    }

    [Benchmark]
    public bool MixedRoutes_ParameterizedHit()
    {
        var result = _mixedTable.Match("POST", _mixedParameterizedPath);
        return result.IsMatch;
    }

    [Benchmark]
    public bool NoMatch()
    {
        var result = _noMatchTable.Match("GET", _noMatchPath);
        return result.IsMatch;
    }
}
