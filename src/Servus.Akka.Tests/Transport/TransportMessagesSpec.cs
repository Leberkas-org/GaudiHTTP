using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport;

public sealed class TransportMessagesSpec
{
    private static readonly ConnectionInfo TestConnectionInfo = new(
        Local: new IPEndPoint(IPAddress.Loopback, 12345),
        Remote: new IPEndPoint(IPAddress.Parse("93.184.216.34"), 443),
        NegotiatedSslProtocol: SslProtocols.Tls13,
        NegotiatedApplicationProtocol: SslApplicationProtocol.Http2);

    [Fact(Timeout = 5000)]
    public void ConnectTransport_should_implement_ITransportOutbound()
    {
        ITransportOutbound msg = new ConnectTransport(new TcpTransportOptions
        {
            Host = "localhost",
            Port = 80
        });

        Assert.IsType<ConnectTransport>(msg);
    }

    [Fact(Timeout = 5000)]
    public void ConnectTransport_should_carry_options()
    {
        var opts = new TlsTransportOptions { Host = "example.com", Port = 443 };
        var msg = new ConnectTransport(opts);

        Assert.Same(opts, msg.Options);
    }

    [Fact(Timeout = 5000)]
    public void DisconnectTransport_should_implement_ITransportOutbound()
    {
        ITransportOutbound msg = new DisconnectTransport(DisconnectReason.Graceful);

        Assert.IsType<DisconnectTransport>(msg);
    }

    [Fact(Timeout = 5000)]
    public void DisconnectTransport_should_carry_reason()
    {
        var msg = new DisconnectTransport(DisconnectReason.Timeout);

        Assert.Equal(DisconnectReason.Timeout, msg.Reason);
    }

    [Fact(Timeout = 5000)]
    public void TransportConnected_should_implement_ITransportInbound()
    {
        ITransportInbound msg = new TransportConnected(TestConnectionInfo);

        Assert.IsType<TransportConnected>(msg);
    }

    [Fact(Timeout = 5000)]
    public void TransportConnected_should_carry_connection_info()
    {
        var msg = new TransportConnected(TestConnectionInfo);

        Assert.Equal(TestConnectionInfo, msg.Info);
    }

    [Fact(Timeout = 5000)]
    public void TransportDisconnected_should_implement_ITransportInbound()
    {
        ITransportInbound msg = new TransportDisconnected(DisconnectReason.Error);

        Assert.IsType<TransportDisconnected>(msg);
    }

    [Fact(Timeout = 5000)]
    public void TransportDisconnected_should_carry_reason()
    {
        var msg = new TransportDisconnected(DisconnectReason.Evicted);

        Assert.Equal(DisconnectReason.Evicted, msg.Reason);
    }

    [Fact(Timeout = 5000)]
    public void TransportError_should_implement_ITransportInbound()
    {
        ITransportInbound msg = new TransportError(new InvalidOperationException("test"), Fatal: true);

        Assert.IsType<TransportError>(msg);
    }

    [Fact(Timeout = 5000)]
    public void TransportError_should_carry_exception_and_fatal_flag()
    {
        var ex = new TimeoutException("timed out");
        var msg = new TransportError(ex, Fatal: false);

        Assert.Same(ex, msg.Exception);
        Assert.False(msg.Fatal);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionInfo_should_expose_all_fields()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 443);

        var info = new ConnectionInfo(local, remote, SslProtocols.Tls12, SslApplicationProtocol.Http11);

        Assert.Equal(local, info.Local);
        Assert.Equal(remote, info.Remote);
        Assert.Equal(SslProtocols.Tls12, info.NegotiatedSslProtocol);
        Assert.Equal(SslApplicationProtocol.Http11, info.NegotiatedApplicationProtocol);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionInfo_should_allow_null_ssl_fields()
    {
        var info = new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 5000),
            new IPEndPoint(IPAddress.Loopback, 80),
            NegotiatedSslProtocol: null,
            NegotiatedApplicationProtocol: null);

        Assert.Null(info.NegotiatedSslProtocol);
        Assert.Null(info.NegotiatedApplicationProtocol);
    }
}
