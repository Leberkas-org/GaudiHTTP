using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;

namespace GaudiHTTP.Server;

/// <summary>
/// TLS/HTTPS configuration applied to a single server endpoint. Specifies the server
/// certificate, optional client-certificate policy, and TLS handshake parameters.
/// </summary>
public sealed class GaudiHttpsOptions
{
    /// <summary>Gets or sets the in-memory server certificate. Takes precedence over <see cref="CertificatePath"/> when both are set.</summary>
    public X509Certificate2? ServerCertificate { get; set; }
    /// <summary>Gets or sets the file-system path to a PEM or PKCS#12 certificate file. Ignored when <see cref="ServerCertificate"/> is set.</summary>
    public string? CertificatePath { get; set; }
    /// <summary>Gets or sets the password used to decrypt the certificate file at <see cref="CertificatePath"/>. Ignored when loading PEM files without encryption.</summary>
    public string? CertificatePassword { get; set; }
    /// <summary>
    /// Gets or sets the TLS protocol versions the server will accept.
    /// Default is <see cref="SslProtocols.None"/>, which lets the OS choose a secure default.
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
    /// <summary>Gets or sets a callback used to validate the client certificate when client authentication is requested.</summary>
    public RemoteCertificateValidationCallback? ClientCertificateValidationCallback { get; set; }
    /// <summary>Gets or sets the maximum time allowed for the TLS handshake to complete. Default is 10 seconds.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the client certificate requirement mode. Default is <see cref="ClientCertificateMode.NoCertificate"/>.</summary>
    public ClientCertificateMode ClientCertificateMode { get; set; } = ClientCertificateMode.NoCertificate;
    /// <summary>
    /// Gets or sets a per-connection certificate selector invoked with the TLS SNI host name.
    /// Takes precedence over <see cref="ServerCertificate"/> when non-null.
    /// Not supported for HTTP/3 (QUIC) endpoints.
    /// </summary>
    public Func<string?, X509Certificate2?>? ServerCertificateSelector { get; set; }
}