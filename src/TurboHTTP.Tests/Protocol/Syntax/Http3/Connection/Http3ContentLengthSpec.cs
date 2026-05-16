using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Connection;

public sealed class Http3ContentLengthSpec
{
    private readonly QpackTableSync _tableSync = new(encoderMaxCapacity: 0);
    private readonly Http3ClientDecoder _decoder;

    public Http3ContentLengthSpec()
    {
        _decoder = new Http3ClientDecoder(_tableSync);
    }

    private HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new HeadersFrame(_tableSync.Encoder.Encode(headers));
    }
}