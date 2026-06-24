using System.Net;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

public sealed class TransportBufferOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Tcp_partial_transport_override_should_fall_back_to_tcp_defaults_per_property()
    {
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5000, listen =>
        {
            listen.Transport = new TransportBufferOptions
            {
                OutputPauseThreshold = 128 * 1024
            };
        });

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(128 * 1024, tcp.OutputPauseThreshold);
        Assert.Equal(1024 * 1024, tcp.InputPauseThreshold);
        Assert.Equal(512 * 1024, tcp.InputResumeThreshold);
        Assert.Equal(32 * 1024, tcp.OutputResumeThreshold);
        Assert.Equal(16 * 1024, tcp.MinimumSegmentSize);
    }

    [Fact(Timeout = 5000)]
    public void Quic_partial_transport_override_should_fall_back_to_quic_defaults_per_property()
    {
        using var cert = CreateSelfSignedCert();
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5001, listen =>
        {
            listen.Protocols = HttpProtocols.Http3;
            listen.UseHttps(cert);
            listen.Transport = new TransportBufferOptions
            {
                InputPauseThreshold = 256 * 1024
            };
        });

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var quic = Assert.IsType<QuicListenerOptions>(binding.Options);

        Assert.Equal(256 * 1024, quic.InputPauseThreshold);
        Assert.Equal(32 * 1024, quic.InputResumeThreshold);
        Assert.Equal(64 * 1024, quic.OutputPauseThreshold);
        Assert.Equal(32 * 1024, quic.OutputResumeThreshold);
        Assert.Equal(4 * 1024, quic.MinimumSegmentSize);
    }

    [Fact(Timeout = 5000)]
    public void Null_transport_should_use_tcp_defaults()
    {
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5002);

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(1024 * 1024, tcp.InputPauseThreshold);
        Assert.Equal(512 * 1024, tcp.InputResumeThreshold);
        Assert.Equal(64 * 1024, tcp.OutputPauseThreshold);
        Assert.Equal(32 * 1024, tcp.OutputResumeThreshold);
        Assert.Equal(16 * 1024, tcp.MinimumSegmentSize);
    }

    [Fact(Timeout = 5000)]
    public void Resolved_input_resume_above_pause_should_throw()
    {
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5003, listen =>
        {
            listen.Transport = new TransportBufferOptions
            {
                InputResumeThreshold = 2 * 1024 * 1024
            };
        });

        Assert.Throws<InvalidOperationException>(() => new EndpointResolver().Resolve(options));
    }

    [Fact(Timeout = 5000)]
    public void Resolved_output_resume_above_pause_should_throw()
    {
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5004, listen =>
        {
            listen.Transport = new TransportBufferOptions
            {
                OutputResumeThreshold = 128 * 1024
            };
        });

        Assert.Throws<InvalidOperationException>(() => new EndpointResolver().Resolve(options));
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
    }
}
