using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Streams;
using HttpVersion = System.Net.HttpVersion;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http11ServerDecoder _decoder;
    private readonly Http11ServerEncoder _encoder;
    private readonly int _maxPipelineDepth;

    private int _requestsPipelined;
    private int _pendingResponseCount;
    private bool _outboundBodyPending;

    public bool CanAcceptResponse => !_outboundBodyPending && _pendingResponseCount > 0;
    public bool ShouldComplete { get; private set; }

    public Http11ServerStateMachine(IServerStageOperations ops, long maxRequestBodySize = 10_485_760, int maxPipelineDepth = 10)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        if (maxPipelineDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPipelineDepth), "Must be greater than zero.");
        }
        _maxPipelineDepth = maxPipelineDepth;

        var decoderOpts = Http11ServerDecoderOptions.Default;
        var encoderOpts = Http11ServerEncoderOptions.Default;

        _decoder = new Http11ServerDecoder(decoderOpts);
        _encoder = new Http11ServerEncoder(encoderOpts);
    }

    public void PreStart()
    {
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        try
        {
            var span = buffer.Memory.Span;
            var pos = 0;

            while (pos < span.Length)
            {
                var outcome = _decoder.Feed(span[pos..], out var consumed);
                pos += consumed;

                if (outcome != DecodeOutcome.Complete)
                {
                    break;
                }

                _requestsPipelined++;
                if (_requestsPipelined > _maxPipelineDepth)
                {
                    ShouldComplete = true;
                    break;
                }

                if (!ShouldComplete && _decoder.HasConnectionClose)
                {
                    ShouldComplete = true;
                }

                var request = _decoder.GetRequest();

                if (!ShouldComplete && request.Version == HttpVersion.Version10)
                {
                    ShouldComplete = true;
                }

                _pendingResponseCount++;
                _ops.OnRequest(request);
                _decoder.Reset();
            }
        }
        catch (Exception)
        {
            ShouldComplete = true;
        }
        finally
        {
            buffer.Dispose();
        }
    }

    public void OnResponse(HttpResponseMessage response)
    {
        if (_pendingResponseCount == 0)
        {
            throw new InvalidOperationException("Cannot send a response when no requests are pending.");
        }

        _pendingResponseCount--;

        if (ShouldComplete)
        {
            response.Headers.Connection.Add(WellKnownHeaders.CloseValue);
        }

        var isChunked = response.Headers.TransferEncoding.Any(te => te.Value == WellKnownHeaders.ChunkedValue);

        var responseBuffer = TransportBuffer.Rent(8192);
        var span = responseBuffer.FullMemory.Span;
        var written = _encoder.Encode(span, response, _ops.StageActor, isChunked, connectionClose: ShouldComplete);
        responseBuffer.Length = written;
        _ops.OnOutbound(new TransportData(responseBuffer));

        if (response.Content is not null)
        {
            _outboundBodyPending = true;
        }
    }

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case OutboundBodyChunk chunk:
                var buf = TransportBuffer.Rent(chunk.Length);
                chunk.Owner.Memory.Span[..chunk.Length].CopyTo(buf.FullMemory.Span);
                buf.Length = chunk.Length;
                chunk.Owner.Dispose();
                _ops.OnOutbound(new TransportData(buf));
                break;

            case OutboundBodyComplete:
                _outboundBodyPending = false;
                break;

            case OutboundBodyFailed failed:
                _outboundBodyPending = false;
                _ops.Log.Warning("Failed to encode HTTP/1.1 response body: {0}", failed.Reason.Message);
                break;
        }
    }

    public void Cleanup()
    {
        _encoder.CancelActiveBody();
        _outboundBodyPending = false;
        _pendingResponseCount = 0;
    }
}
