using GaudiHTTP.Internal;

namespace GaudiHTTP.Protocol.Syntax.Http2.Hpack;

/// <summary>
/// HPACK-specific exception for RFC 7541 protocol violations.
/// </summary>
internal class HpackException(string message) : GaudiProtocolException(message);

