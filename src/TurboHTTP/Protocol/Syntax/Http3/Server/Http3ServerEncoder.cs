using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

/// <summary>
/// Encodes HTTP/3 response messages into HEADERS and DATA frame sequences.
/// Mirrors the client's Http3ClientEncoder but produces responses instead of requests.
/// Stateful: maintains QPACK encoder for connection lifetime.
/// </summary>
internal sealed class Http3ServerEncoder
{
    private readonly QpackTableSync _tableSync;
    private readonly Http3ServerEncoderOptions _options;
    private readonly List<(string Name, string Value)> _reusableHeaders = new(16);

    public Http3ServerEncoder(QpackTableSync tableSync, Http3ServerEncoderOptions options)
    {
        ArgumentNullException.ThrowIfNull(tableSync);
        ArgumentNullException.ThrowIfNull(options);
        _tableSync = tableSync;
        _options = options;
    }

    /// <summary>
    /// Encoder instructions generated during the most recent response encode.
    /// These must be sent on the encoder instruction stream before the response is transmitted.
    /// </summary>
    public ReadOnlyMemory<byte> EncoderInstructions =>
        _tableSync.Encoder.EncoderInstructions;

    /// <summary>
    /// Encodes a response to HTTP/3 HEADERS frame only.
    /// Body is handled asynchronously via PipeTo drain and StreamState outbound buffer.
    /// </summary>
    public HeadersFrame EncodeHeaders(IFeatureCollection features)
    {
        ArgumentNullException.ThrowIfNull(features);

        _reusableHeaders.Clear();
        BuildHeaderList(features, _reusableHeaders, _options);

        var headerBlock = _tableSync.Encoder.Encode(_reusableHeaders);

        return new HeadersFrame(headerBlock);
    }

    private static void BuildHeaderList(IFeatureCollection features, List<(string Name, string Value)> headers, Http3ServerEncoderOptions options)
    {
        // RFC 9114 §6.3: :status pseudo-header (required, must be first)
        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        headers.Add((WellKnownHeaders.Status, WellKnownHeaders.GetStatusCodeString(statusCode)));

        // Add regular headers (lowercase per RFC 9114)
        var responseHeaders = responseFeature?.Headers;
        var sawDate = false;
        if (responseHeaders is not null)
        {
            foreach (var h in responseHeaders)
            {
                if (ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
                {
                    continue;
                }

                if (!sawDate && h.Key.Equals(WellKnownHeaders.Date, StringComparison.OrdinalIgnoreCase))
                {
                    sawDate = true;
                }

                var value = ContentHeaderClassifier.JoinHeaderValues(h.Value);
                headers.Add((ContentHeaderClassifier.ToLowerAscii(h.Key), value));
            }
        }

        if (options.WriteDateHeader && !sawDate)
        {
            headers.Add(("date", DateHeaderCache.GetValue()));
        }
    }
}