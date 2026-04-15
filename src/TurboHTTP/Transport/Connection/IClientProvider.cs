using System.Net;

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Abstracts a raw TCP or TLS connection so that <see cref="ClientState"/> is independent
/// of the underlying transport.
/// </summary>
public interface IClientProvider : IAsyncDisposable
{
    /// <summary>Gets the remote endpoint the socket is connected to, or <see langword="null"/> if not yet connected.</summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>Gets the local endpoint the socket is bound to, or <see langword="null"/> if not yet connected.</summary>
    EndPoint? LocalEndPoint => null;

    /// <summary>Opens a connection to the configured host asynchronously and returns the network stream.</summary>
    Task<Stream> GetStreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Indicates whether this provider supports opening multiple streams on a single connection.
    /// Returns <see langword="true"/> for QUIC (HTTP/3), <see langword="false"/> for TCP/TLS.
    /// </summary>
    bool SupportsMultipleStreams => false;

    /// <summary>
    /// Opens a unidirectional outbound stream on the underlying connection.
    /// Only supported by QUIC transports; TCP/TLS providers throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<Stream> GetUnidirectionalStreamAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Unidirectional streams are only supported by QUIC transports.");

    /// <summary>
    /// Accepts a server-initiated inbound unidirectional stream.
    /// The caller is responsible for reading the stream-type byte from the returned stream.
    /// Only supported by QUIC transports; TCP/TLS providers throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<Stream> AcceptInboundStreamAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Inbound streams are only supported by QUIC transports.");
}