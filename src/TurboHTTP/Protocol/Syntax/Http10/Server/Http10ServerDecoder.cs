using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerDecoder(Http10ServerDecoderOptions options)
{
    private enum Phase
    {
        RequestLine,
        Headers,
        Body,
        Done
    }

    private readonly HeaderBlockReader _headerReader = new(options.MaxHeaderBytes, options.MaxHeaderCount, options.HeaderLineMaxLength, options.AllowObsFold);

    private Phase _phase = Phase.RequestLine;
    private HttpMethod _method = null!;
    private string _target = null!;
    private Version _version = null!;

    public IBodyDecoder? CurrentBodyDecoder { get; private set; }

    public int LastBodyBytesConsumed { get; private set; }

    public DecodeOutcome Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        consumed = 0;
        var pos = 0;

        if (_phase == Phase.RequestLine)
        {
            if (!RequestLineParser.TryParse(data, options.RequestLineMaxLength, out var method, out var target, out var version, out var rlConsumed))
            {
                return DecodeOutcome.NeedMore;
            }

            if (target.Length > options.MaxRequestTargetLength)
            {
                throw new HttpProtocolException(
                    $"Request target length {target.Length} exceeds limit ({options.MaxRequestTargetLength}).");
            }

            _method = method;
            _target = target;
            _version = version;
            pos = rlConsumed.Value;
            _phase = Phase.Headers;
        }

        if (_phase == Phase.Headers)
        {
            var result = _headerReader.Feed(data[pos..], out var hConsumed);
            pos += hConsumed;
            if (result == HeaderBlockResult.NeedMore)
            {
                consumed = pos;
                return DecodeOutcome.NeedMore;
            }

            var classification = BodySemantics.ClassifyRequest(_method, _headerReader.GetHeaders(), _version);
            CurrentBodyDecoder = BodyDecoderFactory.Create(classification, options.ToBodyDecoderOptions());
            _phase = Phase.Body;
        }

        if (_phase == Phase.Body)
        {
            var done = CurrentBodyDecoder!.Feed(data[pos..], out var bConsumed);
            LastBodyBytesConsumed = bConsumed;
            pos += bConsumed;
            consumed = pos;
            if (done)
            {
                _phase = Phase.Done;
                return DecodeOutcome.Complete;
            }

            return DecodeOutcome.NeedMore;
        }

        consumed = pos;
        return DecodeOutcome.Complete;
    }

    public TurboHttpRequestFeature GetRequestFeature()
    {
        var body = CurrentBodyDecoder?.GetBodyStream() ?? Stream.Null;

        var feature = new TurboHttpRequestFeature
        {
            Protocol = _version switch
            {
                { Major: 1, Minor: 0 } => "HTTP/1.0",
                _ => "HTTP/1.1"
            },
            Method = _method.Method,
            Path = ParsePath(_target),
            QueryString = ParseQueryString(_target),
            RawTarget = _target,
            Body = body,
        };

        // Populate directly into the feature's header dictionary, avoiding a throwaway
        // HeaderDictionary allocation plus the copy loop in the Headers setter.
        HeaderRouter.ApplyToHeaderDictionary(feature.Headers, _headerReader.GetHeaders());
        return feature;
    }

    private static string ParsePath(string target)
    {
        var queryIdx = target.IndexOf('?');
        var pathPart = queryIdx >= 0 ? target[..queryIdx] : target;
        return string.IsNullOrEmpty(pathPart) ? "/" : pathPart;
    }

    private static string ParseQueryString(string target)
    {
        var queryIdx = target.IndexOf('?');
        return queryIdx >= 0 ? target[queryIdx..] : string.Empty;
    }
}