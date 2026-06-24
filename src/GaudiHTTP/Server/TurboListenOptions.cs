using System.Net;
using System.Security.Cryptography.X509Certificates;
using Akka.Routing;

namespace GaudiHTTP.Server;

/// <summary>
/// Configures a single server listen endpoint: the IP address, port, HTTP protocols, and
/// optional TLS settings. Obtained from <see cref="Listen"/> overloads.
/// </summary>
public sealed class TurboListenOptions(IPAddress address, ushort port)
{
    /// <summary>Gets the IP address this endpoint listens on.</summary>
    public IPAddress Address { get; } = address;
    /// <summary>Gets the TCP/UDP port this endpoint listens on.</summary>
    public ushort Port { get; } = port;
    /// <summary>Gets or sets the HTTP protocol versions enabled on this endpoint. Default is HTTP/1.x and HTTP/2.</summary>
    public HttpProtocols Protocols { get; set; } = HttpProtocols.Http1AndHttp2;

    internal bool IsHttps => HttpsOptions is not null;
    internal GaudiHttpsOptions? HttpsOptions { get; private set; }

    /// <summary>Enables HTTPS using the default <see cref="GaudiHttpsOptions"/> (certificate must be supplied via <see cref="TurboServerOptions.ConfigureHttpsDefaults"/>).</summary>
    public void UseHttps()
    {
        HttpsOptions = new GaudiHttpsOptions();
    }

    /// <summary>Enables HTTPS using the provided <paramref name="certificate"/>.</summary>
    public void UseHttps(X509Certificate2 certificate)
    {
        HttpsOptions = new GaudiHttpsOptions { ServerCertificate = certificate };
    }

    /// <summary>Enables HTTPS by loading the certificate from the file at <paramref name="path"/>, optionally decrypting with <paramref name="password"/>.</summary>
    public void UseHttps(string path, string? password = null)
    {
        HttpsOptions = new GaudiHttpsOptions
        {
            CertificatePath = path,
            CertificatePassword = password
        };
    }

    /// <summary>Enables HTTPS and applies additional TLS settings via <paramref name="configure"/>.</summary>
    public void UseHttps(Action<GaudiHttpsOptions> configure)
    {
        HttpsOptions = new GaudiHttpsOptions();
        configure(HttpsOptions);
    }

    /// <summary>Enables HTTPS with <paramref name="certificate"/> and applies additional TLS settings via <paramref name="configure"/>.</summary>
    public void UseHttps(X509Certificate2 certificate, Action<GaudiHttpsOptions> configure)
    {
        HttpsOptions = new GaudiHttpsOptions { ServerCertificate = certificate };
        configure(HttpsOptions);
    }

    /// <summary>Enables HTTPS from a certificate file at <paramref name="path"/> and applies additional TLS settings via <paramref name="configure"/>.</summary>
    public void UseHttps(string path, string? password, Action<GaudiHttpsOptions> configure)
    {
        HttpsOptions = new GaudiHttpsOptions
        {
            CertificatePath = path,
            CertificatePassword = password
        };
        configure(HttpsOptions);
    }

    /// <summary>
    /// Gets the transport-level buffer options for this endpoint. Controls backpressure
    /// thresholds on the read/write pipes between the OS socket and the HTTP pipeline.
    /// Defaults are protocol-optimized: TCP uses larger buffers (one pipe per connection),
    /// QUIC uses smaller buffers (one pipe per stream).
    /// Set to <c>null</c> to use the protocol-specific defaults.
    /// </summary>
    public TransportBufferOptions? Transport { get; set; }

    internal string? ConnectionLoggingCategory { get; private set; }

    /// <summary>Enables per-connection logging under the default category <c>GaudiHTTP.Server.ConnectionLogging</c>.</summary>
    public void UseConnectionLogging()
    {
        ConnectionLoggingCategory = "GaudiHTTP.Server.ConnectionLogging";
    }

    /// <summary>Enables per-connection logging under the specified <paramref name="loggerName"/> category.</summary>
    public void UseConnectionLogging(string loggerName)
    {
        ConnectionLoggingCategory = loggerName;
    }
}