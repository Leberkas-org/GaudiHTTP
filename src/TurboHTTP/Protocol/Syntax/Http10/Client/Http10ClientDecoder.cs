using System.Net;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientDecoder(Http10ClientDecoderOptions options, ConnectionPoolContext poolContext)
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
    private Version _version = null!;
    private int _statusCode;
    private string _reason = null!;
    private IBodyReader? _bodyReader;
    private IFramingDecoder? _framingDecoder;
    private IStreamingBodyReader? _streamingReader;
    private HttpResponseMessage? _response;
    private bool _isHttp09;

    public bool IsBodyStreaming => _phase == Phase.Body && !_isHttp09 && _streamingReader is not null;

    public bool IsQueueFull => _streamingReader?.IsFull ?? false;

    public IStreamingBodyReader? StreamingReader => _streamingReader;

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
                _version = HttpVersion.Version10;
                _statusCode = 200;
                _reason = "OK";
                var buffered = poolContext.Rent(static () => new BufferedBodyReader());
                buffered.ResetOpenEnded();
                _bodyReader = buffered;
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
            var classification = BodySemantics.ClassifyResponse(
                _statusCode, headers, _version, requestMethodWasHead,
                connectionWillClose: !ConnectionSemantics.IsPersistent(headers, _version));

            if (classification.Framing == BodyFraming.Close)
            {
                var buffered = poolContext.Rent(static () => new BufferedBodyReader());
                buffered.ResetOpenEnded();
                _bodyReader = buffered;
                _framingDecoder = null;
            }
            else
            {
                var readerClassification = BodyReaderClassification.FromBodyClassification(
                    classification, options.ToBodyDecoderOptions());
                var (reader, decoder) = poolContext.RentBodyReader(readerClassification, options.ToBodyDecoderOptions());
                _bodyReader = reader;
                _framingDecoder = decoder;
                if (reader is IStreamingBodyReader streaming)
                {
                    _streamingReader = streaming;
                }
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

            if (_streamingReader is not null && _framingDecoder is not null)
            {
                var remaining = span[pos..];
                while (remaining.Length > 0)
                {
                    var result = _framingDecoder.Decode(remaining, out var rawConsumed);
                    pos += rawConsumed;

                    if (!result.Body.IsEmpty)
                    {
                        _streamingReader.TryEnqueue(result.Body);
                    }

                    if (result.EndOfBody)
                    {
                        _streamingReader.Complete();
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
        if (_streamingReader is not null && _framingDecoder is not null)
        {
            var ok = _framingDecoder.OnEof();
            if (ok)
            {
                _streamingReader.Complete();
            }
            else
            {
                _streamingReader.Fault(new HttpRequestException(
                    "Connection closed before the complete response body was received."));
            }

            return ok;
        }

        if (_framingDecoder is not null)
        {
            return _framingDecoder.OnEof();
        }

        if (_bodyReader is BufferedBodyReader buffered && !buffered.IsCompleted)
        {
            if (buffered.IsOpenEnded)
            {
                buffered.MarkComplete();
                return true;
            }

            return false;
        }

        return false;
    }

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
            Content = content
        };
        HeaderRouter.ApplyToResponse(msg, _headerReader.GetHeaders());
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
        _streamingReader = null;
        _response = null;
        _isHttp09 = false;
        _headerReader.Reset();
    }

    private static bool IsLikelyHttpResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length >= WellKnownHeaders.Http.Bytes.Length)
        {
            return data[..WellKnownHeaders.Http.Bytes.Length].SequenceEqual(WellKnownHeaders.Http);
        }

        return WellKnownHeaders.Http.Bytes.Span[..data.Length].SequenceEqual(data);
    }
}
