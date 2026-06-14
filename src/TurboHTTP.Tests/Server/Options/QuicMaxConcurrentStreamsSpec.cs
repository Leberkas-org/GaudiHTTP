using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Options;

/// <summary>
/// The QUIC listener must bound concurrent request streams at the configured
/// <see cref="Http3ServerOptions.MaxConcurrentStreams"/>. Previously the binding never set
/// <see cref="QuicListenerOptions.MaxInboundBidirectionalStreams"/>, so the transport defaulted
/// to 100 regardless of configuration.
/// </summary>
public sealed class QuicMaxConcurrentStreamsSpec
{
    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void Quic_binding_should_reflect_configured_max_concurrent_streams()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.Http3.MaxConcurrentStreams = 42;
        options.Listen(IPAddress.Loopback, 5051, listen =>
        {
            listen.Protocols = HttpProtocols.Http3;
            listen.UseHttps(cert);
        });

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var quic = Assert.IsType<QuicListenerOptions>(binding.Options);

        Assert.Equal(42, quic.MaxInboundBidirectionalStreams);
    }
}
