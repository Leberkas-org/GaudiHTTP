using System.Net;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientDecoder(Http11ClientDecoderOptions options)
{
    private enum Phase
    {
        StatusLine,
        Headers,
        Body,
        Done
    }

    private readonly HeaderBlockReader _headerReader = new(
        options.MaxHeaderBytes, options.MaxHeaderCount, options.HeaderLineMaxLength, options.AllowObsFold);

    private Phase _phase = Phase.StatusLine;
    private bool _bodyCompletedByEof;
    private Version _version = null!;
    private int _statusCode;
    private string _reason = null!;
    private IBodyReader? _bodyReader;
    private IFramingDecoder? _framingDecoder;
    private HttpResponseMessage? _response;
    private bool _isHttp09;

    public bool ConnectionWillClose { get; private set; }

    public bool IsBodyStreaming => _phase == Phase.Body && !_isHttp09 && StreamingReader is not null;

    public bool IsQueueFull => StreamingReader?.IsFull ?? false;

    public IStreamingBodyReader? StreamingReader { get; private set; }

    internal bool HasActiveBody => _phase == Phase.Body;

    private static ReadOnlySpan<byte> HttpSlashPrefix => WellKnownHeaders.Http.Bytes.Span;

    public DecodeOutcome Feed(ReadOnlyMemory<byte> data, bool requestMethodWasHead, out int consumed)
    {
        consumed = 0;
        var pos = 0;
        var span = data.Span;

        if (_phase == Phase.StatusLine)
        {
            if (span.Length > 0 && !IsLikelyHttpResponse(span))
            {
                _isHttp09 = true;
                _version = HttpVersion.Version11;
                _statusCode = 200;
                _reason = "OK";

                var (reader, decoder) = BodyReaderFactory.Create(
                    new BodyClassification(BodyFraming.Close, null),
                    options.ToBodyDecoderOptions());
                _bodyReader = reader;
                _framingDecoder = decoder;
                if (reader is IStreamingBodyReader streaming)
                {
                    StreamingReader = streaming;
                }

                _phase = Phase.Body;
            }
            else
            {
                if (!StatusLineParser.TryParse(span, out var ver, out var code, out var reason, out var slConsumed))
                {
                    return DecodeOutcome.NeedMore;
                }

                _version = ver;
                _statusCode = code;
                _reason = reason;
                pos = slConsumed;
                _phase = Phase.Headers;
            }
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
            ConnectionWillClose = !ConnectionSemantics.IsPersistent(headers, _version);
            var classification = BodySemantics.ClassifyResponse(
                _statusCode, headers, _version, requestMethodWasHead,
                connectionWillClose: ConnectionWillClose);

            var (reader, decoder) = BodyReaderFactory.Create(classification, options.ToBodyDecoderOptions());
            _bodyReader = reader;
            _framingDecoder = decoder;
            if (reader is IStreamingBodyReader streaming)
            {
                StreamingReader = streaming;
            }

            _phase = Phase.Body;
        }

        if (_phase == Phase.Body)
        {
            if (_bodyReader is BufferedBodyReader buffered)
            {
                var take = buffered.Feed(span[pos..]);
                pos += take;
                consumed = pos;
                if (buffered.IsCompleted)
                {
                    _phase = Phase.Done;
                    return DecodeOutcome.Complete;
                }

                return _isHttp09 ? DecodeOutcome.HeadersReady : DecodeOutcome.NeedMore;
            }

            if (StreamingReader is not null && _framingDecoder is not null)
            {
                var remaining = span[pos..];
                while (remaining.Length > 0)
                {
                    var result = _framingDecoder.Decode(remaining, out var rawConsumed);
                    pos += rawConsumed;

                    if (!result.Body.IsEmpty)
                    {
                        StreamingReader.TryEnqueue(result.Body);
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
                return _isHttp09 ? DecodeOutcome.HeadersReady : DecodeOutcome.NeedMore;
            }

            consumed = pos;
            return DecodeOutcome.Complete;
        }

        consumed = pos;
        return DecodeOutcome.Complete;
    }

    public bool SignalEof()
    {
        if (StreamingReader is not null && _framingDecoder is not null)
        {
            var ok = _framingDecoder.OnEof();
            if (ok)
            {
                StreamingReader.Complete();
            }

            _bodyCompletedByEof = ok;
            return ok;
        }

        if (_framingDecoder is not null)
        {
            _bodyCompletedByEof = _framingDecoder.OnEof();
            return _bodyCompletedByEof;
        }

        return false;
    }

    internal bool IsBodyComplete => _phase == Phase.Done || _bodyCompletedByEof;

    public HttpResponseMessage GetResponse()
    {
        if (_response is not null)
        {
            return _response;
        }

        HttpContent content;
        var bodyStream = _bodyReader?.AsStream();
        if (bodyStream is not null)
        {
            content = new StreamContent(bodyStream);
        }
        else
        {
            content = new ByteArrayContent([]);
        }

        var msg = new HttpResponseMessage((HttpStatusCode)_statusCode)
        {
            Version = _version,
            ReasonPhrase = _reason,
            Content = content,
        };
        HeaderRouter.ApplyToResponse(msg, _headerReader.GetHeaders());
        if (_framingDecoder?.Trailers is { Count: > 0 } trailers)
        {
            foreach (var (name, value) in trailers)
            {
                msg.TrailingHeaders.TryAddWithoutValidation(name, value);
            }
        }

        _response = msg;
        return msg;
    }

    public void Reset()
    {
        _phase = Phase.StatusLine;
        _version = null!;
        _statusCode = 0;
        _reason = null!;
        _bodyReader = null;
        _framingDecoder = null;
        StreamingReader = null;
        _response = null;
        _isHttp09 = false;
        ConnectionWillClose = false;
        _bodyCompletedByEof = false;
        _headerReader.Reset();
    }

    private static bool IsLikelyHttpResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length >= HttpSlashPrefix.Length)
        {
            return data[..HttpSlashPrefix.Length].SequenceEqual(HttpSlashPrefix);
        }

        return HttpSlashPrefix[..data.Length].SequenceEqual(data);
    }
}
