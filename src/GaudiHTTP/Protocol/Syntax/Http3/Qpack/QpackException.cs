using GaudiHTTP.Internal;

namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

/// <summary>
/// Exception thrown for QPACK protocol violations (RFC 9204).
/// </summary>
internal sealed class QpackException(string message) : TurboProtocolException(message);

