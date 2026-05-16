using Akka.Actor;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientEncoder
{
    private readonly Http11ClientEncoderOptions _options;

    public Http11ClientEncoder(Http11ClientEncoderOptions options)
    {
        options.Validate();
        _options = options;
    }

    public int Encode(Span<byte> destination, HttpRequestMessage request, IActorRef stageActor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        RequestValidator.Validate(request);

        var bodyEncoder = BodyEncoderFactory.Create(
            request.Content,
            request.Version,
            request.Headers);

        var writer = SpanWriter.Create(destination);
        var targetStr = request.ResolveTarget();
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, request.Version);
        var headers = HeaderBuilder.Build(request);
        HeaderBlockWriter.Write(ref writer, headers);

        bodyEncoder?.Start(request.Content!, stageActor);

        return writer.BytesWritten;
    }
}