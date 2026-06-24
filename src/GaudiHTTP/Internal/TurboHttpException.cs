namespace GaudiHTTP.Internal;

/// <summary>
/// Base class for all GaudiHttp exceptions.
/// Catch this type to handle any error originating from the GaudiHttp library.
/// </summary>
internal abstract class GaudiHttpException : Exception
{
    protected GaudiHttpException(string message) : base(message)
    {
    }

    protected GaudiHttpException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base class for protocol-level exceptions (RFC violations, malformed frames, compression errors).
/// Catch this type to handle any protocol error across HTTP/1.x, HTTP/2, HTTP/3, HPACK, and QPACK.
/// </summary>
internal abstract class TurboProtocolException : GaudiHttpException
{
    protected TurboProtocolException(string message) : base(message)
    {
    }

    protected TurboProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base class for transport-level exceptions (connection failures, abrupt disconnects).
/// Catch this type to handle any connection or transport error.
/// </summary>
internal abstract class TurboTransportException(string message) : GaudiHttpException(message);

