using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// TLS connection options, extending <see cref="TcpOptions"/> with certificate and protocol settings.
/// </summary>
public record TlsOptions : TcpOptions
{
    public string? TargetHost { get; init; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
    public List<SslApplicationProtocol>? ApplicationProtocols { get; init; }
}