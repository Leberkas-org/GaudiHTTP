using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerDecoder(Http11ServerDecoderOptions options, ConnectionPoolContext poolContext)
{
    private enum Phase
    {
        RequestLine,
        Headers,
        Body,
        Done
    }

    private readonly HeaderBlockReader _headerReader = new(options.MaxHeaderBytes, options.MaxHeaderCount, options.HeaderLineMaxLength, options.AllowObsFold);

    // Projected once: the source options are immutable, so the per-request projection produced a
    // fresh identical BodyDecoderOptions record every request (allocated even for no-body GETs,
    // where BodyReaderFactory.Create never reads it).
    private readonly BodyDecoderOptions _bodyDecoderOptions = options.ToBodyDecoderOptions();

    private Phase _phase = Phase.RequestLine;
    private HttpMethod _method = null!;
    private string _target = null!;
    private Version _version = null!;

    public IBodyReader? CurrentBodyReader { get; private set; }
    public IFramingDecoder? CurrentFramingDecoder { get; private set; }
    public IStreamingBodyReader? StreamingReader { get; private set; }
    public bool IsQueueFull => StreamingReader?.IsFull ?? false;

    public DecodeOutcome Feed(ReadOnlyMemory<byte> data, out int consumed)
    {
        consumed = 0;
        var pos = 0;
        var span = data.Span;

        if (_phase == Phase.RequestLine)
        {
            if (!RequestLineParser.TryParse(span, options.RequestLineMaxLength, out var method, out var target, out var version, out var rlConsumed))
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
            var result = _headerReader.Feed(span[pos..], out var hConsumed);
            pos += hConsumed;
            if (result == HeaderBlockResult.NeedMore)
            {
                consumed = pos;
                return DecodeOutcome.NeedMore;
            }

            var headers = _headerReader.GetHeaders();
            if (!headers.Contains(WellKnownHeaders.ContentLength) && !headers.Contains(WellKnownHeaders.TransferEncoding))
            {
                _phase = Phase.Done;
                consumed = pos;
                return DecodeOutcome.Complete;
            }

            var classification = BodySemantics.ClassifyRequest(_method, headers, _version);
            var readerClassification = BodyReaderClassification.FromBodyClassification(classification, _bodyDecoderOptions);
            var (reader, decoder) = poolContext.RentBodyReader(readerClassification, _bodyDecoderOptions);
            CurrentBodyReader = reader;
            CurrentFramingDecoder = decoder;
            if (reader is IStreamingBodyReader streaming)
            {
                StreamingReader = streaming;
            }

            if (CurrentBodyReader is null || (CurrentBodyReader is BufferedBodyReader { IsCompleted: true }))
            {
                _phase = Phase.Done;
                consumed = pos;
                return DecodeOutcome.Complete;
            }

            _phase = Phase.Body;

            if (CurrentBodyReader is not BufferedBodyReader)
            {
                consumed = pos;
                return DecodeOutcome.HeadersReady;
            }
        }

        if (_phase == Phase.Body)
        {
            if (CurrentBodyReader is BufferedBodyReader bufferedBody)
            {
                var take = bufferedBody.Feed(span[pos..]);
                pos += take;
                consumed = pos;
                if (bufferedBody.IsCompleted)
                {
                    _phase = Phase.Done;
                    return DecodeOutcome.Complete;
                }

                return DecodeOutcome.NeedMore;
            }

            if (StreamingReader is not null && CurrentFramingDecoder is not null)
            {
                var remaining = span[pos..];
                while (remaining.Length > 0)
                {
                    var result = CurrentFramingDecoder.Decode(remaining, out var rawConsumed);
                    pos += rawConsumed;

                    if (!result.Body.IsEmpty)
                    {
                        if (!StreamingReader.TryEnqueue(result.Body))
                        {
                            if (result.EndOfBody)
                            {
                                StreamingReader.Complete();
                                _phase = Phase.Done;
                                consumed = pos;
                                return DecodeOutcome.Complete;
                            }

                            consumed = pos;
                            return DecodeOutcome.NeedMore;
                        }
                    }

                    if (result.EndOfBody)
                    {
                        StreamingReader.Complete();
                        _phase = Phase.Done;
                        consumed = pos;
                        return DecodeOutcome.Complete;
                    }

                    if (rawConsumed == 0)
                    {
                        break;
                    }

                    remaining = span[pos..];
                }

                consumed = pos;
                return DecodeOutcome.NeedMore;
            }

            consumed = pos;
            return DecodeOutcome.Complete;
        }

        consumed = pos;
        return DecodeOutcome.Complete;
    }

    public bool HasConnectionClose
    {
        get
        {
            foreach (var v in _headerReader.GetHeaders().GetValues(WellKnownHeaders.Connection))
            {
                if (ConnectionHeaderSemantics.HasCloseOption(v))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public TurboHttpRequestFeature GetRequestFeature()
    {
        var feature = new TurboHttpRequestFeature();
        PopulateRequestFeature(feature);
        return feature;
    }

    public void PopulateRequestFeature(TurboHttpRequestFeature feature)
    {
        feature.Protocol = _version switch
        {
            { Major: 1, Minor: 0 } => WellKnownHeaders.Http10,
            { Major: 1, Minor: 1 } => WellKnownHeaders.Http11,
            _ => WellKnownHeaders.Http11
        };
        feature.Method = _method.Method;
        SplitTarget(_target, out var path, out var query);
        feature.Path = path;
        feature.QueryString = query;
        feature.RawTarget = _target;
        feature.Body = CurrentBodyReader?.AsStream() ?? Stream.Null;

        HeaderRouter.ApplyToHeaderDictionary(feature.Headers, _headerReader.GetHeaders());
    }

    private static void SplitTarget(string target, out string path, out string query)
    {
        var queryIdx = target.IndexOf('?');
        if (queryIdx < 0)
        {
            path = string.IsNullOrEmpty(target) ? "/" : target;
            query = string.Empty;
        }
        else
        {
            var pathPart = target[..queryIdx];
            path = string.IsNullOrEmpty(pathPart) ? "/" : pathPart;
            query = target[queryIdx..];
        }
    }

    public void Reset()
    {
        _phase = Phase.RequestLine;
        _method = null!;
        _target = null!;
        _version = null!;
        CurrentBodyReader = null;
        CurrentFramingDecoder = null;
        StreamingReader = null;
        _headerReader.Reset();
    }
}
