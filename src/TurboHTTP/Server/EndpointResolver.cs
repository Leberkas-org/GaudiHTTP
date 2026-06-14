using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic.Listener;
using Servus.Akka.Transport.Tcp.Listener;

namespace TurboHTTP.Server;

internal sealed class EndpointResolver
{
    public IReadOnlyList<ListenerBinding> Resolve(TurboServerOptions options)
    {
        var allListenOptions = new List<TurboListenOptions>(options.ListenOptions);

        foreach (var url in options.Urls)
        {
            allListenOptions.Add(ParseUrl(url));
        }

        var bindings = new List<ListenerBinding>();

        foreach (var listen in allListenOptions)
        {
            if (listen.IsHttps)
            {
                ApplyHttpsDefaults(listen.HttpsOptions!, options.HttpsDefaultsCallback);
                var cert = ResolveCertificate(listen.HttpsOptions!);

                if (cert is null && listen.HttpsOptions!.ServerCertificateSelector is null)
                {
                    throw new InvalidOperationException(
                        string.Concat(
                            "No server certificate configured for HTTPS endpoint '",
                            listen.Address, ":", listen.Port.ToString(),
                            "'. Provide a certificate via UseHttps() or ServerCertificateSelector."));
                }

                var tcpProtocols = listen.Protocols & ~HttpProtocols.Http3;
                if (tcpProtocols != HttpProtocols.None)
                {
                    bindings.Add(CreateTcpBinding(listen, cert, tcpProtocols, options.Limits.MaxRequestBufferSize));
                }

                if ((listen.Protocols & HttpProtocols.Http3) != 0)
                {
                    if (cert is null)
                    {
                        throw new InvalidOperationException(
                            "HTTP/3 requires a static certificate. ServerCertificateSelector is not supported for QUIC.");
                    }

                    bindings.Add(CreateQuicBinding(listen, cert, options.Http3.MaxConcurrentStreams));
                }
            }
            else
            {
                if ((listen.Protocols & HttpProtocols.Http3) != 0)
                {
                    throw new InvalidOperationException(
                        string.Concat(
                            "HTTP/3 requires HTTPS. Configure a certificate for endpoint '",
                            listen.Address, ":", listen.Port.ToString(), "'."));
                }

                bindings.Add(CreateTcpBinding(listen, certificate: null, listen.Protocols,
                    options.Limits.MaxRequestBufferSize));
            }
        }

        foreach (var existing in options.Endpoints)
        {
            bindings.Add(existing);
        }

        return bindings;
    }

    internal static TurboListenOptions ParseUrl(string url)
    {
        var normalizedUrl = url;
        if (url.Contains("://*:") || url.Contains("://+:"))
        {
            normalizedUrl = url.Replace("://*:", "://localhost:").Replace("://+:", "://localhost:");
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            throw new FormatException(
                string.Concat("Invalid endpoint URL '", url, "'. Expected format: 'http(s)://host:port'."));
        }

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new NotSupportedException(
                string.Concat("Unsupported URL scheme '", uri.Scheme, "'. Only 'http' and 'https' are supported."));
        }

        IPAddress address;

        if (url.Contains("://*:") || url.Contains("://+:"))
        {
            address = IPAddress.Any;
        }
        else if (IPAddress.TryParse(uri.Host, out var parsed))
        {
            address = parsed;
        }
        else if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            address = IPAddress.Loopback;
        }
        else
        {
            address = IPAddress.Any;
        }

        var port = (ushort)uri.Port;
        var listenOptions = new TurboListenOptions(address, port);

        if (uri.Scheme == "https")
        {
            listenOptions.UseHttps();
        }

        return listenOptions;
    }

    private static void ApplyHttpsDefaults(TurboHttpsOptions httpsOptions, Action<TurboHttpsOptions>? defaultsCallback)
    {
        if (defaultsCallback is null)
        {
            return;
        }

        var defaults = new TurboHttpsOptions();
        defaultsCallback(defaults);

        httpsOptions.ServerCertificate ??= defaults.ServerCertificate;
        httpsOptions.CertificatePath ??= defaults.CertificatePath;
        httpsOptions.CertificatePassword ??= defaults.CertificatePassword;
        httpsOptions.ClientCertificateValidationCallback ??= defaults.ClientCertificateValidationCallback;
        httpsOptions.ServerCertificateSelector ??= defaults.ServerCertificateSelector;

        if (httpsOptions.EnabledSslProtocols == SslProtocols.None)
        {
            httpsOptions.EnabledSslProtocols = defaults.EnabledSslProtocols;
        }

        if (httpsOptions.HandshakeTimeout == TimeSpan.FromSeconds(10) &&
            defaults.HandshakeTimeout != TimeSpan.FromSeconds(10))
        {
            httpsOptions.HandshakeTimeout = defaults.HandshakeTimeout;
        }

        if (httpsOptions.ClientCertificateMode == ClientCertificateMode.NoCertificate &&
            defaults.ClientCertificateMode != ClientCertificateMode.NoCertificate)
        {
            httpsOptions.ClientCertificateMode = defaults.ClientCertificateMode;
        }
    }

    private static X509Certificate2? ResolveCertificate(TurboHttpsOptions httpsOptions)
    {
        if (httpsOptions.ServerCertificate is not null)
        {
            return httpsOptions.ServerCertificate;
        }

        if (httpsOptions.CertificatePath is not null)
        {
            if (!File.Exists(httpsOptions.CertificatePath))
            {
                throw new FileNotFoundException(
                    string.Concat("Certificate file '", httpsOptions.CertificatePath, "' not found."),
                    httpsOptions.CertificatePath);
            }

            var extension = Path.GetExtension(httpsOptions.CertificatePath);
            if (extension.Equals(".pem", StringComparison.OrdinalIgnoreCase))
            {
                return X509Certificate2.CreateFromPemFile(httpsOptions.CertificatePath);
            }

            return X509CertificateLoader
                .LoadPkcs12FromFile(httpsOptions.CertificatePath, httpsOptions.CertificatePassword);
        }

        return null;
    }

    private static ListenerBinding CreateTcpBinding(TurboListenOptions listen, X509Certificate2? certificate,
        HttpProtocols protocols, long? maxRequestBufferSize)
    {
        var alpn = protocols.ToAlpnProtocols();
        var httpsOptions = listen.HttpsOptions;

        var transport = listen.Transport?.ResolveTcp() ?? TransportBufferOptions.TcpDefaults;
        var (inputPause, inputResume) = ResolveTcpInputThresholds(listen, transport, maxRequestBufferSize);
        var tcpOptions = new TcpListenerOptions
        {
            Host = listen.Address.ToString(),
            Port = listen.Port,
            ServerCertificate = certificate,
            EnabledSslProtocols = httpsOptions?.EnabledSslProtocols ?? SslProtocols.None,
            ApplicationProtocols = alpn.Count > 0 ? alpn : null,
            ClientCertificateValidationCallback = httpsOptions?.ClientCertificateValidationCallback,
            HandshakeTimeout = httpsOptions?.HandshakeTimeout ?? TimeSpan.FromSeconds(10),
            ClientCertificateMode = httpsOptions?.ClientCertificateMode ?? ClientCertificateMode.NoCertificate,
            ServerCertificateSelector = httpsOptions?.ServerCertificateSelector,
            InputPauseThreshold = inputPause,
            InputResumeThreshold = inputResume,
            OutputPauseThreshold = transport.OutputPauseThreshold,
            OutputResumeThreshold = transport.OutputResumeThreshold,
            MinimumSegmentSize = transport.MinimumSegmentSize
        };

        return new ListenerBinding
        {
            Options = tcpOptions,
            Factory = new TcpListenerFactory(),
            ConnectionLoggingCategory = listen.ConnectionLoggingCategory
        };
    }

    /// <summary>
    /// Resolves the TCP read-pipe pause/resume thresholds, applying the server-wide
    /// <see cref="TurboServerLimits.MaxRequestBufferSize"/> as the input-pause default. A
    /// per-listener <see cref="TransportBufferOptions.InputPauseThreshold"/> takes precedence;
    /// a null limit falls through to the transport default. (QUIC is per-stream and is not driven
    /// by this connection-scoped limit.)
    /// </summary>
    private static (long InputPause, long InputResume) ResolveTcpInputThresholds(
        TurboListenOptions listen, ResolvedTransportBuffers transport, long? maxRequestBufferSize)
    {
        if (listen.Transport?.InputPauseThreshold is not null || maxRequestBufferSize is not { } cap)
        {
            return (transport.InputPauseThreshold, transport.InputResumeThreshold);
        }

        // Keep the resume threshold below the (possibly smaller) pause to preserve hysteresis.
        var inputResume = Math.Min(transport.InputResumeThreshold, cap / 2);
        return (cap, inputResume);
    }

    private static ListenerBinding CreateQuicBinding(TurboListenOptions listen, X509Certificate2 certificate,
        int maxConcurrentStreams)
    {
        var transport = listen.Transport?.ResolveQuic() ?? TransportBufferOptions.QuicDefaults;
        var quicOptions = new QuicListenerOptions
        {
            Host = listen.Address.ToString(),
            Port = listen.Port,
            ServerCertificate = certificate,
            ApplicationProtocols = [SslApplicationProtocol.Http3],
            EnabledSslProtocols = listen.HttpsOptions?.EnabledSslProtocols ??
                                  SslProtocols.None,
            ClientCertificateValidationCallback = listen.HttpsOptions?.ClientCertificateValidationCallback,
            // RFC 9114 §6.1 / §7.2.4.2: bound concurrent request streams at the QUIC transport so
            // the listener stops accepting new bidirectional streams past the configured limit.
            MaxInboundBidirectionalStreams = maxConcurrentStreams,
            InputPauseThreshold = transport.InputPauseThreshold,
            InputResumeThreshold = transport.InputResumeThreshold,
            OutputPauseThreshold = transport.OutputPauseThreshold,
            OutputResumeThreshold = transport.OutputResumeThreshold,
            MinimumSegmentSize = transport.MinimumSegmentSize
        };

        return new ListenerBinding
        {
            Options = quicOptions,
            Factory = new QuicListenerFactory(),
            ConnectionLoggingCategory = listen.ConnectionLoggingCategory
        };
    }

}