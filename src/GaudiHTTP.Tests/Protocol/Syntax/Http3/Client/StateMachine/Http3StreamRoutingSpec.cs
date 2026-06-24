using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3StreamRoutingSpec
{
    private readonly FakeClientOps _clientOps = new();
    private readonly QpackTableSync _tableSync = new(0, 4096, 100, null);

    private Http3ClientStateMachine CreateMachine(FakeClientOps? ops = null)
    {
        return new Http3ClientStateMachine(
            new TurboClientOptions(),
            ops ?? _clientOps);
    }

    private HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    private TransportBuffer BuildResponseBuffer(byte fillByte, int bodySize)
    {
        var headersFrame = EncodeHeaders((":status", "200"));
        var body = new byte[bodySize];
        Array.Fill(body, fillByte);
        var dataFrame = new DataFrame(body);

        var totalSize = headersFrame.SerializedSize + dataFrame.SerializedSize;
        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        headersFrame.WriteTo(ref span);
        dataFrame.WriteTo(ref span);
        buf.Length = totalSize;
        return buf;
    }

    private static TransportBuffer BuildDataBuffer(byte fillByte, int bodySize)
    {
        var body = new byte[bodySize];
        Array.Fill(body, fillByte);
        var dataFrame = new DataFrame(body);

        var buf = TransportBuffer.Rent(dataFrame.SerializedSize);
        var span = buf.FullMemory.Span;
        dataFrame.WriteTo(ref span);
        buf.Length = dataFrame.SerializedSize;
        return buf;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public async Task DecodeServerData_should_use_per_stream_decoders()
    {
        var sm = CreateMachine();

        // Stream 0: HEADERS + 1KB DATA
        var buf0 = BuildResponseBuffer(0xAA, 1024);
        sm.DecodeServerData(MultiplexedData.Rent(buf0, 0));

        // Stream 4: HEADERS + 1KB DATA
        var buf4 = BuildResponseBuffer(0xBB, 1024);
        sm.DecodeServerData(MultiplexedData.Rent(buf4, 4));

        // Signal EOF to flush responses
        sm.DecodeServerData(new StreamReadCompleted(0));
        sm.DecodeServerData(new StreamReadCompleted(4));

        // Verify responses were assembled with correct data integrity
        Assert.Equal(2, _clientOps.Responses.Count);

        // Verify stream 0's response body is all 0xAA
        var body0 = await _clientOps.Responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(body0.All(b => b == 0xAA), "Stream 0 body corrupted");

        // Verify stream 4's response body is all 0xBB
        var body4 = await _clientOps.Responses[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(body4.All(b => b == 0xBB), "Stream 4 body corrupted");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public async Task AssembleResponse_should_route_data_to_correct_stream_with_60KB_bodies()
    {
        var sm = CreateMachine();
        const int bodySize = 60 * 1024; // 60KB per stream

        // QueuedBodyReader has a fixed slot capacity: once full, TryEnqueue returns false.
        // Send full body in a single HEADERS+DATA buffer per stream, interleaved across
        // streams, to verify routing correctness without exceeding the queue capacity.

        // Stream 0: HEADERS + 60KB DATA (filled with 0xAA)
        var buf0 = BuildResponseBuffer(0xAA, bodySize);
        sm.DecodeServerData(MultiplexedData.Rent(buf0, 0));

        // Stream 4: HEADERS + 60KB DATA (filled with 0xBB)
        var buf4 = BuildResponseBuffer(0xBB, bodySize);
        sm.DecodeServerData(MultiplexedData.Rent(buf4, 4));

        // Signal EOF to flush both responses
        sm.DecodeServerData(new StreamReadCompleted(0));
        sm.DecodeServerData(new StreamReadCompleted(4));

        Assert.Equal(2, _clientOps.Responses.Count);

        // Verify stream 0 response body is all 0xAA
        var body0 = await _clientOps.Responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodySize, body0.Length);
        Assert.True(body0.All(b => b == 0xAA), "Stream 0 body corrupted — contains bytes from another stream");

        // Verify stream 4 response body is all 0xBB
        var body4 = await _clientOps.Responses[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodySize, body4.Length);
        Assert.True(body4.All(b => b == 0xBB), "Stream 4 body corrupted — contains bytes from another stream");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void DecodeServerData_should_handle_fragmented_data_across_multiple_calls()
    {
        var sm = CreateMachine();

        // Test verifies that per-stream decoders buffer incomplete frames correctly.
        // Send a complete response as fragments to stream 0.

        var response = BuildResponseBuffer(0xCC, 512);
        var bytes = response.FullMemory;

        // Split response into 3 parts
        var part1Size = bytes.Length / 3;
        var part2Size = bytes.Length / 3;
        var part3Size = bytes.Length - part1Size - part2Size;

        var part1 = TransportBuffer.Rent(part1Size);
        bytes.Span.Slice(0, part1Size).CopyTo(part1.FullMemory.Span);
        part1.Length = part1Size;

        var part2 = TransportBuffer.Rent(part2Size);
        bytes.Span.Slice(part1Size, part2Size).CopyTo(part2.FullMemory.Span);
        part2.Length = part2Size;

        var part3 = TransportBuffer.Rent(part3Size);
        bytes.Span.Slice(part1Size + part2Size, part3Size).CopyTo(part3.FullMemory.Span);
        part3.Length = part3Size;

        // Feed fragments to stream 0
        sm.DecodeServerData(MultiplexedData.Rent(part1, 0));
        sm.DecodeServerData(MultiplexedData.Rent(part2, 0));
        sm.DecodeServerData(MultiplexedData.Rent(part3, 0));

        // Signal EOF
        sm.DecodeServerData(new StreamReadCompleted(0));

        // Response should be assembled despite fragmentation
        Assert.Single(_clientOps.Responses);
        var body = _clientOps.Responses[0].Content.ReadAsStream(TestContext.Current.CancellationToken);
        var buffer = new byte[512];
        var bytesRead = body.Read(buffer);
        Assert.Equal(512, bytesRead);
        Assert.True(buffer.All(b => b == 0xCC), "Response body corrupted");

        response.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void DecodeServerData_should_isolate_control_stream_from_request_streams()
    {
        var sm = CreateMachine();
        const long controlStreamId = -2; // Matches ControlStreamDecoderId in Http30ConnectionStage

        // Feed SETTINGS on control stream
        var settings = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 8192)]);
        var settingsBuf = TransportBuffer.Rent(settings.SerializedSize);
        var settingsSpan = settingsBuf.FullMemory.Span;
        settings.WriteTo(ref settingsSpan);
        settingsBuf.Length = settings.SerializedSize;

        sm.DecodeServerData(MultiplexedData.Rent(settingsBuf, controlStreamId));

        // Feed HEADERS + DATA on request stream 0 — should not be affected by control stream
        var reqBuf = BuildResponseBuffer(0xDD, 512);
        sm.DecodeServerData(MultiplexedData.Rent(reqBuf, 0));

        // Flush to get response
        sm.DecodeServerData(new StreamReadCompleted(0));

        Assert.Single(_clientOps.Responses);
        var bodyBuffer = new byte[512];
        var bodyStream = _clientOps.Responses[0].Content.ReadAsStream(TestContext.Current.CancellationToken);
        var bytesRead = bodyStream.Read(bodyBuffer);
        var body = bodyBuffer.Take(bytesRead).ToArray();
        Assert.Equal(512, body.Length);
        Assert.True(body.All(b => b == 0xDD), "Request stream data corrupted by control stream");
    }
}