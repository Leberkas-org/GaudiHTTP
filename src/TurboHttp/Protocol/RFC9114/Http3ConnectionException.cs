using System;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// Thrown when an HTTP/3 connection-level error is detected.
/// Carries the appropriate <see cref="Http3ErrorCode"/> so the transport
/// layer can close the connection with the correct error code.
/// </summary>
public sealed class Http3ConnectionException : Exception
{
    public Http3ErrorCode ErrorCode { get; }

    public Http3ConnectionException(Http3ErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public Http3ConnectionException(Http3ErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
