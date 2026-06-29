using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

/// <summary>
/// HTTP/3 mutual-TLS parity: ClientCertificateMode and HandshakeTimeout configured on the HTTPS
/// options must flow through to the QUIC listener. Previously CreateQuicBinding dropped both, so
/// HTTP/3 client-certificate auth and handshake bounds were silently unsupported.
/// </summary>
public sealed class Http3MutualTlsSpec
{
    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
    }

    private static QuicListenerOptions ResolveQuic(Action<GaudiHttpsOptions> configureHttps)
    {
        using var cert = CreateSelfSignedCert();
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5055, listen =>
        {
            listen.Protocols = HttpProtocols.Http3;
            listen.UseHttps(cert, configureHttps);
        });

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        return Assert.IsType<QuicListenerOptions>(binding.Options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.1")]
    public void Quic_binding_should_carry_client_certificate_mode()
    {
        var quic = ResolveQuic(https => https.ClientCertificateMode = ClientCertificateMode.RequireCertificate);

        Assert.Equal(ClientCertificateMode.RequireCertificate, quic.ClientCertificateMode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.1")]
    public void Quic_binding_should_carry_handshake_timeout()
    {
        var quic = ResolveQuic(https => https.HandshakeTimeout = TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(3), quic.HandshakeTimeout);
    }
}
