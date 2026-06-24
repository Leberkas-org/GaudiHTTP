using GaudiHTTP.Internal;

namespace GaudiHTTP.Protocol;

/// <summary>
/// A protocol violation. For the line-based protocols (HTTP/1.0, 1.1) this is connection-fatal by
/// nature. For the multiplexed protocols (HTTP/2, 3) prefer the scoped subtypes
/// <see cref="ConnectionProtocolException"/> / <see cref="StreamProtocolException"/>, which carry the
/// wire error code so the session manager can emit an accurate GOAWAY/RST_STREAM and tear down.
/// </summary>
internal class HttpProtocolException(string message) : TurboProtocolException(message);

/// <summary>
/// A connection-fatal protocol error (RFC 9113 §5.4.1 / RFC 9114 §8). The peer must be sent a
/// GOAWAY/CONNECTION_CLOSE carrying <see cref="ErrorCode"/> and the connection torn down.
/// <see cref="ErrorCode"/> is the raw wire value (an <c>Http2ErrorCode</c> or HTTP/3 error code).
/// </summary>
internal sealed class ConnectionProtocolException(int errorCode, string message)
    : HttpProtocolException(message)
{
    public int ErrorCode { get; } = errorCode;
}

/// <summary>
/// A stream-scoped protocol error (RFC 9113 §5.4.2 / RFC 9114 §8). The offending stream is reset with
/// <see cref="ErrorCode"/> while the connection survives.
/// </summary>
internal sealed class StreamProtocolException(int streamId, int errorCode, string message)
    : HttpProtocolException(message)
{
    public int StreamId { get; } = streamId;
    public int ErrorCode { get; } = errorCode;
}
