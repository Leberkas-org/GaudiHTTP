using System.Net.Security;
using System.Security.Authentication;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Server.Context.Features;

public sealed class TlsHandshakeFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void TlsHandshakeFeature_should_expose_protocol()
    {
        var feature = new TlsHandshakeFeature
        {
            Protocol = SslProtocols.Tls13
        };
        Assert.Equal(SslProtocols.Tls13, feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void TlsHandshakeFeature_should_expose_negotiated_cipher_suite()
    {
        var feature = new TlsHandshakeFeature
        {
            NegotiatedCipherSuite = TlsCipherSuite.TLS_AES_256_GCM_SHA384
        };
        Assert.Equal(TlsCipherSuite.TLS_AES_256_GCM_SHA384, feature.NegotiatedCipherSuite);
    }

    [Fact(Timeout = 5000)]
    public void TlsHandshakeFeature_should_expose_hostname()
    {
        var feature = new TlsHandshakeFeature
        {
            HostName = "example.com"
        };
        Assert.Equal("example.com", feature.HostName);
    }

    [Fact(Timeout = 5000)]
    public void TlsHandshakeFeature_should_expose_negotiated_application_protocol()
    {
        var feature = new TlsHandshakeFeature
        {
            NegotiatedApplicationProtocol = SslApplicationProtocol.Http2
        };
        Assert.Equal(SslApplicationProtocol.Http2, feature.NegotiatedApplicationProtocol);
    }

    [Fact(Timeout = 5000)]
    public void TlsHandshakeFeature_should_default_cipher_suite_to_null()
    {
        var feature = new TlsHandshakeFeature();
        Assert.Null(feature.NegotiatedCipherSuite);
    }

    [Fact(Timeout = 5000)]
    public void TlsHandshakeFeature_should_default_hostname_to_null()
    {
        var feature = new TlsHandshakeFeature();
        Assert.Null(feature.HostName);
    }
}
