using System.Diagnostics;
using Servus.Akka.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class ServusInstrumentationSpec : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public ServusInstrumentationSpec()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ServusInstrumentation.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        foreach (var activity in _activities)
        {
            if (!activity.IsStopped)
            {
                activity.Stop();
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void Source_should_have_correct_name()
    {
        Assert.Equal("Servus.Akka", ServusInstrumentation.Source.Name);
        Assert.Equal("Servus.Akka", ServusInstrumentation.SourceName);
    }

    [Fact(Timeout = 5000)]
    public void StartConnect_should_create_activity()
    {
        var activity = ServusInstrumentation.StartConnect(new Uri("https://example.com:8443/"));

        Assert.NotNull(activity);
        Assert.Equal("Servus.Akka.Connect", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("example.com", activity.GetTagItem("server.address"));
        Assert.Equal(8443, activity.GetTagItem("server.port"));
        Assert.Equal("https", activity.GetTagItem("url.scheme"));
    }

    [Fact(Timeout = 5000)]
    public void StartDnsLookup_should_create_activity()
    {
        var activity = ServusInstrumentation.StartDnsLookup("example.com");

        Assert.NotNull(activity);
        Assert.Equal("Servus.Akka.DnsLookup", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("example.com", activity.GetTagItem("dns.question.name"));
    }

    [Fact(Timeout = 5000)]
    public void StartSocketConnect_should_create_activity()
    {
        var activity = ServusInstrumentation.StartSocketConnect("93.184.216.34", 443);

        Assert.NotNull(activity);
        Assert.Equal("Servus.Akka.SocketConnect", activity.OperationName);
        Assert.Equal("93.184.216.34", activity.GetTagItem("network.peer.address"));
        Assert.Equal(443, activity.GetTagItem("network.peer.port"));
        Assert.Equal("tcp", activity.GetTagItem("network.transport"));
    }

    [Fact(Timeout = 5000)]
    public void StartSocketConnect_should_set_network_type_when_provided()
    {
        var activity = ServusInstrumentation.StartSocketConnect("93.184.216.34", 443, "tcp", "ipv4");

        Assert.NotNull(activity);
        Assert.Equal("ipv4", activity.GetTagItem("network.type"));
    }

    [Fact(Timeout = 5000)]
    public void StartSocketConnect_should_omit_network_type_when_null()
    {
        var activity = ServusInstrumentation.StartSocketConnect("93.184.216.34", 443);

        Assert.NotNull(activity);
        Assert.Null(activity.GetTagItem("network.type"));
    }

    [Fact(Timeout = 5000)]
    public void StartTlsHandshake_should_create_activity()
    {
        var activity = ServusInstrumentation.StartTlsHandshake("example.com");

        Assert.NotNull(activity);
        Assert.Equal("Servus.Akka.TlsHandshake", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("example.com", activity.GetTagItem("server.address"));
    }

    [Fact(Timeout = 5000)]
    public void StartWaitForConnection_should_create_activity()
    {
        var activity = ServusInstrumentation.StartWaitForConnection("example.com", 443);

        Assert.NotNull(activity);
        Assert.Equal("Servus.Akka.WaitForConnection", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("example.com", activity.GetTagItem("server.address"));
        Assert.Equal(443, activity.GetTagItem("server.port"));
    }

    [Fact(Timeout = 5000)]
    public void SetTlsInfo_should_set_protocol_tags()
    {
        var activity = ServusInstrumentation.StartTlsHandshake("example.com");
        Assert.NotNull(activity);

        ServusInstrumentation.SetTlsInfo(activity, "tls", "1.3");

        Assert.Equal("tls", activity.GetTagItem("tls.protocol.name"));
        Assert.Equal("1.3", activity.GetTagItem("tls.protocol.version"));
    }

    [Fact(Timeout = 5000)]
    public void SetDnsAnswers_should_set_answers_tag()
    {
        var activity = ServusInstrumentation.StartDnsLookup("example.com");
        Assert.NotNull(activity);

        ServusInstrumentation.SetDnsAnswers(activity, ["93.184.216.34", "2606:2800:220:1::"]);

        Assert.Equal(new[] { "93.184.216.34", "2606:2800:220:1::" }, activity.GetTagItem("dns.answers"));
    }

    [Fact(Timeout = 5000)]
    public void SetNetworkPeerAddress_should_set_tag()
    {
        var activity = ServusInstrumentation.StartConnect(new Uri("https://example.com/"));
        Assert.NotNull(activity);

        ServusInstrumentation.SetNetworkPeerAddress(activity, "93.184.216.34");

        Assert.Equal("93.184.216.34", activity.GetTagItem("network.peer.address"));
    }

    [Fact(Timeout = 5000)]
    public void SetError_should_set_error_status_and_type()
    {
        var activity = ServusInstrumentation.StartConnect(new Uri("https://example.com/"));
        Assert.NotNull(activity);

        var ex = new InvalidOperationException("test error");
        ServusInstrumentation.SetError(activity, ex);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact(Timeout = 5000)]
    public void StartConnect_should_return_null_when_no_listener()
    {
        _listener.Dispose();
        var activity = ServusInstrumentation.StartConnect(new Uri("https://example.com/"));
        Assert.Null(activity);
    }
}
