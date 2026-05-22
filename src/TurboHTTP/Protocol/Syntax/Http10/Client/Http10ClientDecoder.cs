using System.Net;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientDecoder
{
    private enum Phase
    {
        StatusLine,
        Headers,
        Body,
        Done
    }

    private readonly Http10ClientDecoderOptions _options;
    private readonly HeaderBlockReader _headerReader;

    private Phase _phase = Phase.StatusLine;
    private Version _version = null!;
    private int _statusCode;
    private string _reason = null!;
    private IBodyDecoder? _bodyDecoder;
    private HttpResponseMessage? _response;
    private bool _isHttp09;

    public Http10ClientDecoder(Http10ClientDecoderOptions options)
    {
        options.Validate();
        _options = options;
        var s = options.Shared;
        _headerReader =
            new HeaderBlockReader(s.MaxHeaderBytes, s.MaxHeaderCount, s.HeaderLineMaxLength, s.AllowObsFold);
    }

    public DecodeOutcome Feed(ReadOnlySpan<byte> data, bool requestMethodWasHead, out int consumed)
    {
        consumed = 0;
        var pos = 0;

        if (_phase == Phase.StatusLine)
        {
            if (data.Length > 0 && !IsLikelyHttpResponse(data))
            {
                _isHttp09 = true;
                _version = HttpVersion.Version10;
                _statusCode = 200;
                _reason = "OK";
                _bodyDecoder = new CloseDelimitedBodyDecoder();
                _phase = Phase.Body;
            }
            else
            {
                if (!StatusLineParser.TryParse(data, out var ver, out var code, out var reason, out var slConsumed))
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
            var result = _headerReader.Feed(data[pos..], out var hConsumed);
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

            _bodyDecoder = BodyDecoderFactory.Create(
                classification,
                _options.Shared.StreamingThreshold,
                _options.Shared.BufferPool,
                _options.Shared.MaxBufferedBodySize,
                _options.Shared.MaxStreamedBodySize);

            _phase = Phase.Body;
        }

        if (_phase == Phase.Body)
        {
            var slice = data[pos..];
            var done = _bodyDecoder!.Feed(slice, out var bConsumed);
            pos += bConsumed;
            consumed = pos;
            if (done)
            {
                _phase = Phase.Done;
                return DecodeOutcome.Complete;
            }

            return _isHttp09 ? DecodeOutcome.HeadersReady : DecodeOutcome.NeedMore;
        }

        consumed = pos;
        return DecodeOutcome.Complete;
    }

    public bool SignalEof() => _bodyDecoder?.OnEof() ?? false;

    public HttpResponseMessage GetResponse()
    {
        if (_response is not null)
        {
            return _response;
        }

        HttpContent content;
        var bodyStream = _bodyDecoder?.GetBodyStream();
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
        _response = msg;
        return msg;
    }

    public void Reset()
    {
        _phase = Phase.StatusLine;
        _version = null!;
        _statusCode = 0;
        _reason = null!;
        _bodyDecoder = null;
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