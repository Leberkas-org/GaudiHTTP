using System.Buffers;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http3;
using Xunit;
using FrameDecoder = GaudiHTTP.Protocol.Syntax.Http2.FrameDecoder;

namespace GaudiHTTP.Tests.Shared;

public abstract class EngineTestBase : StreamTestBase
{
    internal static WireBuffer ToWireBuffer(byte[] data)
    {
        var buffer = WireBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    internal static TestConnectionStage CreateFakeConnection(Func<byte[]> responseFactory)
    {
        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                var transport = new TestPipeTransport();
                ctx.SetTransport(transport);
                transport.OnOutputReceived(outputBytes =>
                {
                    var requestCount = CountHttpRequests(outputBytes);
                    return requestCount > 0 ? ConcatResponses(responseFactory, requestCount) : null;
                });
                ctx.Push(new TransportConnected(ConnectionInfo.None, transport));
            })
            .Build();
        return stage;
    }

    private static int CountHttpRequests(byte[] data)
    {
        var span = data.AsSpan();
        var terminator = "\r\n\r\n"u8;
        var count = 0;
        var pos = 0;
        while (pos < span.Length)
        {
            var idx = span[pos..].IndexOf(terminator);
            if (idx < 0)
            {
                break;
            }

            count++;
            pos += idx + 4;
        }

        return count;
    }

    private static byte[] ConcatResponses(Func<byte[]> factory, int count)
    {
        if (count == 1)
        {
            return factory();
        }

        var responses = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            responses.Add(factory());
        }

        var total = responses.Sum(r => r.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var r in responses)
        {
            r.CopyTo(result, offset);
            offset += r.Length;
        }

        return result;
    }

    internal static Flow<ITransportOutbound, ITransportInbound, NotUsed> CreateFakeConnectionFlow(
        Func<byte[]> responseFactory)
        => CreateFakeConnection(responseFactory).AsFlow();

    internal static TestConnectionStage CreateScriptedConnection(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                var transport = new TestPipeTransport();
                ctx.SetTransport(transport);
                transport.OnOutputReceived(requestBytes =>
                {
                    var response = responseFactory(index++, requestBytes);
                    if (response is null)
                    {
                        transport.CompleteInput();
                    }

                    return response;
                });
                ctx.Push(new TransportConnected(ConnectionInfo.None, transport));
            })
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateAccumulatingScriptedConnection(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var accumulated = new List<byte>();

        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                var transport = new TestPipeTransport();
                ctx.SetTransport(transport);
                transport.OnOutputReceived(requestBytes =>
                {
                    accumulated.AddRange(requestBytes);

                    var (headerEnd, contentLength) = TryParseRequest(accumulated);
                    if (headerEnd < 0)
                    {
                        return null;
                    }

                    var totalExpected = headerEnd + 4 + contentLength;
                    if (accumulated.Count < totalExpected)
                    {
                        return null;
                    }

                    var completeRequest = accumulated.GetRange(0, totalExpected).ToArray();
                    accumulated.RemoveRange(0, totalExpected);

                    var response = responseFactory(index++, completeRequest);
                    if (response is null)
                    {
                        transport.CompleteInput();
                    }

                    return response;
                });
                ctx.Push(new TransportConnected(ConnectionInfo.None, transport));
            })
            .Build();
        return stage;

        static (int HeaderEnd, int ContentLength) TryParseRequest(List<byte> bytes)
        {
            var arr = bytes.ToArray();
            var headerEnd = arr.AsSpan().IndexOf("\r\n\r\n"u8);
            if (headerEnd < 0)
            {
                return (-1, 0);
            }

            var headerStr = Encoding.Latin1.GetString(arr, 0, headerEnd);
            var contentLength = 0;
            foreach (var line in headerStr.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                    break;
                }
            }

            return (headerEnd, contentLength);
        }
    }

    internal static TestConnectionStage CreateScriptedConnectionWithClose(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                var transport = new TestPipeTransport();
                ctx.SetTransport(transport);
                transport.OnOutputReceived(requestBytes =>
                {
                    var response = responseFactory(index++, requestBytes);
                    if (response is null)
                    {
                        transport.CompleteInput();
                        return null;
                    }

                    Task.Run(async () =>
                    {
                        await transport.FeedInputAsync(response);
                        transport.CompleteInput();
                    });
                    return null;
                });
                ctx.Push(new TransportConnected(ConnectionInfo.None, transport));
            })
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateProxyConnection(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                var transport = new TestPipeTransport();
                ctx.SetTransport(transport);
                transport.OnOutputReceived(requestBytes =>
                {
                    var response = responseFactory(index++, requestBytes);
                    if (response is null)
                    {
                        transport.CompleteInput();
                    }

                    return response;
                });
                ctx.Push(new TransportConnected(ConnectionInfo.None, transport));
            })
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateH2Connection(params byte[][] serverFrames)
    {
        var frameIndex = 0;
        var outputCount = 0;

        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                var transport = new TestPipeTransport();
                ctx.SetTransport(transport);
                transport.OnOutputReceived(_ =>
                {
                    outputCount++;
                    if (outputCount == 1)
                    {
                        return null;
                    }

                    return frameIndex < serverFrames.Length ? serverFrames[frameIndex++] : null;
                });
                ctx.Push(new TransportConnected(ConnectionInfo.None, transport));
            })
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateH3Connection(params byte[][] serverFrames)
    {
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<CompleteWrites>((_, ctx) =>
            {
                for (var i = 0; i < serverFrames.Length; i++)
                {
                    var buf = serverFrames[i];
                    if (i == 0)
                    {
                        ctx.Push(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
                        ctx.Push(MultiplexedData.Rent(ToWireBuffer(buf), 3));
                    }
                    else
                    {
                        ctx.Push(MultiplexedData.Rent(ToWireBuffer(buf), 0));
                    }
                }

                if (serverFrames.Length > 1)
                {
                    ctx.Push(new StreamReadCompleted(0));
                }
            })
            .Build();
        return stage;
    }

    internal async Task<(HttpResponseMessage Response, string RawRequest)> SendAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound,
            ITransportInbound, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        Func<byte[]> responseFactory)
    {
        var stage = CreateFakeConnection(responseFactory);

        var response = await TestPipeline.RunAsync(
            engine.Join(stage.AsFlow()), request, Materializer,
            ct: TestContext.Current.CancellationToken);

        var rawRequest = stage.Transport is { } transport
            ? Encoding.Latin1.GetString(transport.CapturedOutputBytes)
            : string.Empty;

        return (response, rawRequest);
    }

    internal async Task<(IReadOnlyList<HttpResponseMessage> Responses, string RawRequests)> SendManyAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
        IEnumerable<HttpRequestMessage> requests,
        Func<byte[]> responseFactory,
        int expectedCount)
    {
        var stage = CreateFakeConnection(responseFactory);

        var results = await TestPipeline.RunManyAsync(
            engine.Join(stage.AsFlow()), requests, expectedCount, Materializer, ct:
            TestContext.Current.CancellationToken);

        var rawRequests = stage.Transport is { } transport
            ? Encoding.Latin1.GetString(transport.CapturedOutputBytes)
            : string.Empty;

        return (results, rawRequests);
    }

    internal async Task<(HttpResponseMessage Response, IReadOnlyList<Http2Frame> OutboundFrames)> SendH2EngineAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var stage = CreateH2Connection(serverFrames);
        var flow = engine.Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Wait for body encoder to finish sending DATA frames (async via actor messages).
        // The body encoder is started in a fire-and-forget task which may not complete
        // before the response is returned. Give the actor system time to process all messages.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var outboundBytes = DrainOutboundBytes(stage, stripH2Preface: true);

        var frames = outboundBytes.Length > 0
            ? new FrameDecoder().DecodeAll(new ReadOnlySequence<byte>(outboundBytes), out _)
            : [];

        return (response, frames);
    }

    internal async Task<(List<HttpResponseMessage> Responses, IReadOnlyList<Http2Frame> OutboundFrames)>
        SendH2EngineAsyncMany(
            BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
            IEnumerable<HttpRequestMessage> requests,
            int expectedCount,
            params byte[][] serverFrames)
    {
        var stage = CreateH2Connection(serverFrames);
        var flow = engine.Join(stage.AsFlow());

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var outboundBytes = DrainOutboundBytes(stage, stripH2Preface: true);

        var frames = outboundBytes.Length > 0
            ? new FrameDecoder().DecodeAll(new ReadOnlySequence<byte>(outboundBytes), out _)
            : [];

        return (results.ToList(), frames);
    }

    internal async Task<(HttpResponseMessage Response, IReadOnlyList<Http3Frame> OutboundFrames)> SendH3EngineAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var stage = CreateH3Connection(serverFrames);
        var flow = engine.Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Wait for body encoder to finish sending DATA frames (async via actor messages)
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var requestBytes = new List<byte>();
        var controlBytes = new List<byte>();
        while (stage.TryGetOutbound(out var outbound))
        {
            switch (outbound)
            {
                case MultiplexedData mux:
                    var bytes = mux.Buffer.Span.ToArray();
                    switch (mux.StreamId)
                    {
                        case -2:
                            controlBytes.AddRange(bytes);
                            break;
                        case -4:
                        case -3:
                            break;
                        default:
                            requestBytes.AddRange(bytes);
                            break;
                    }

                    mux.Return();
                    break;
                case TransportData { Buffer: var dataBuf }:
                    requestBytes.AddRange(dataBuf.Span.ToArray());
                    break;
            }
        }

        var frames = new List<Http3Frame>();

        if (requestBytes.Count > 0)
        {
            frames.AddRange(new Protocol.Syntax.Http3.FrameDecoder().DecodeAll(new ReadOnlySequence<byte>(requestBytes.ToArray()), out _));
        }

        if (controlBytes.Count > 0)
        {
            var controlSpan = controlBytes.ToArray().AsSpan();
            if (controlSpan.Length > 0 && controlSpan[0] == 0x00)
            {
                controlSpan = controlSpan[1..];
            }

            if (controlSpan.Length > 0)
            {
                frames.AddRange(new Protocol.Syntax.Http3.FrameDecoder().DecodeAll(new ReadOnlySequence<byte>(controlSpan.ToArray()), out _));
            }
        }

        return (response, frames);
    }

    private static byte[] DrainOutboundBytes(TestConnectionStage stage, bool stripH2Preface)
    {
        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        if (stage.Transport is not { } transport)
        {
            return [];
        }

        var bytes = transport.CapturedOutputBytes;
        if (!stripH2Preface || bytes.Length < 24)
        {
            return bytes;
        }

        var span = bytes.AsSpan();
        if (span[..24].SequenceEqual(preface))
        {
            return span[24..].ToArray();
        }

        return bytes;
    }
}