using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP.Server;

/// <summary>
/// Configures a single server listen endpoint: the IP address, port, HTTP protocols, and
/// optional TLS settings. Obtained from <see cref="TurboServerOptions.Listen"/> overloads.
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
    internal TurboHttpsOptions? HttpsOptions { get; private set; }

    /// <summary>Enables HTTPS using the default <see cref="TurboHttpsOptions"/> (certificate must be supplied via <see cref="TurboServerOptions.ConfigureHttpsDefaults"/>).</summary>
    public void UseHttps()
    {
        HttpsOptions = new TurboHttpsOptions();
    }

    /// <summary>Enables HTTPS using the provided <paramref name="certificate"/>.</summary>
    public void UseHttps(X509Certificate2 certificate)
    {
        HttpsOptions = new TurboHttpsOptions { ServerCertificate = certificate };
    }

    /// <summary>Enables HTTPS by loading the certificate from the file at <paramref name="path"/>, optionally decrypting with <paramref name="password"/>.</summary>
    public void UseHttps(string path, string? password = null)
    {
        HttpsOptions = new TurboHttpsOptions
        {
            CertificatePath = path,
            CertificatePassword = password
        };
    }

    /// <summary>Enables HTTPS and applies additional TLS settings via <paramref name="configure"/>.</summary>
    public void UseHttps(Action<TurboHttpsOptions> configure)
    {
        HttpsOptions = new TurboHttpsOptions();
        configure(HttpsOptions);
    }

    /// <summary>Enables HTTPS with <paramref name="certificate"/> and applies additional TLS settings via <paramref name="configure"/>.</summary>
    public void UseHttps(X509Certificate2 certificate, Action<TurboHttpsOptions> configure)
    {
        HttpsOptions = new TurboHttpsOptions { ServerCertificate = certificate };
        configure(HttpsOptions);
    }

    /// <summary>Enables HTTPS from a certificate file at <paramref name="path"/> and applies additional TLS settings via <paramref name="configure"/>.</summary>
    public void UseHttps(string path, string? password, Action<TurboHttpsOptions> configure)
    {
        HttpsOptions = new TurboHttpsOptions
        {
            CertificatePath = path,
            CertificatePassword = password
        };
        configure(HttpsOptions);
    }

    internal string? ConnectionLoggingCategory { get; private set; }

    /// <summary>Enables per-connection logging under the default category <c>TurboHTTP.Server.ConnectionLogging</c>.</summary>
    public void UseConnectionLogging()
    {
        ConnectionLoggingCategory = "TurboHTTP.Server.ConnectionLogging";
    }

    /// <summary>Enables per-connection logging under the specified <paramref name="loggerName"/> category.</summary>
    public void UseConnectionLogging(string loggerName)
    {
        ConnectionLoggingCategory = loggerName;
    }
}