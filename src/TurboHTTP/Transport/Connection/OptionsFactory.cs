using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.Transport.Connection;

internal static class OptionsFactory
{
    private static bool IsHttp3(Version? requestVersion)
    {
        return requestVersion is { Major: 3, Minor: 0 };
    }

    internal static TcpOptions Build(RequestEndpoint endpoint, TurboClientOptions clientOptions)
    {
        var isTls = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        var port = endpoint.Port != 0 ? endpoint.Port : isTls ? 443 : 80;

        if (IsHttp3(endpoint.Version))
        {
            return new QuicOptions
            {
                Host = endpoint.Host,
                Port = port,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.Http2.MaxFrameSize,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
                AllowConnectionMigration = clientOptions.Http3.AllowConnectionMigration,
            };
        }

        if (isTls)
        {
            return new TlsOptions
            {
                Host = endpoint.Host,
                Port = port,
                TargetHost = endpoint.Host,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ClientCertificates = clientOptions.ClientCertificates,
                EnabledSslProtocols = clientOptions.EnabledSslProtocols,
                ConnectTimeout = clientOptions.ConnectTimeout,
                MaxFrameSize = clientOptions.Http2.MaxFrameSize,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
            };
        }

        return new TcpOptions
        {
            Host = endpoint.Host,
            Port = port,
            ConnectTimeout = clientOptions.ConnectTimeout,
            MaxFrameSize = clientOptions.Http2.MaxFrameSize,
            SocketSendBufferSize = clientOptions.SocketSendBufferSize,
            SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
        };
    }
}