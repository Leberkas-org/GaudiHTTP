using TurboHTTP.Internal;

namespace TurboHTTP.Protocol;

internal sealed class HttpProtocolException(string message) : TurboProtocolException(message);
