namespace TurboHttp.Transport;

internal static class TcpOptionsFactory
{
    private static bool IsTls(this Uri value)
    {
        return string.Equals(value.Scheme, "https", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttp3(Version? requestVersion)
    {
        return requestVersion is { Major: 3, Minor: 0 };
    }

    internal static TcpOptions Build(Uri requestUri, TurboClientOptions clientOptions, Version? requestVersion = null)
    {
        var host = requestUri.Host;
        var isTls = requestUri.IsTls();
        int port;
        if (requestUri.Port is not -1)
        {
            port = requestUri.Port;
        }
        else
        {
            port = isTls ? 443 : 80;
        }

        if (IsHttp3(requestVersion))
        {
            return new QuicOptions
            {
                Host = host,
                Port = port,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        if (isTls)
        {
            return new TlsOptions
            {
                Host = host,
                Port = port,
                TargetHost = host,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ClientCertificates = clientOptions.ClientCertificates,
                EnabledSslProtocols = clientOptions.EnabledSslProtocols,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.MaxFrameSize,
            };
        }

        return new TcpOptions
        {
            Host = host,
            Port = port,
            ConnectTimeout = clientOptions.ConnectTimeout,
            MaxFrameSize = clientOptions.MaxFrameSize,
        };
    }
}