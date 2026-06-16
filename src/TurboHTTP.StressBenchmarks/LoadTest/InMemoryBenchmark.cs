using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Pooling;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Benchmarks.LoadTest;

internal static class InMemoryBenchmark
{
    public static void Run(LoadTestOptions options)
    {
        var protocol = options.Protocol;
        Console.WriteLine(string.Concat("In-memory benchmark | protocol=", protocol,
            " duration=", options.DurationSeconds.ToString(), "s warmup=", options.WarmupSeconds.ToString(), "s"));
        Console.WriteLine("Mode: state-machine-only (no Akka, no transport, no ASP.NET).");
        Console.WriteLine(new string('-', 80));

        switch (protocol)
        {
            case "mem-h1":
                RunH11(options);
                break;
            case "mem-h2":
                RunH2(options);
                break;
            default:
                Console.WriteLine(string.Concat("Unknown in-memory protocol: ", protocol));
                break;
        }
    }

    private static void RunH11(LoadTestOptions options)
    {
        var serverOptions = new TurboServerOptions();
        var ops = new BenchmarkServerOps();
        var sm = new Http11ServerStateMachine(
            serverOptions.ToHttp1Options(), serverOptions.ToHttp2Options(), ops);

        var request = Encoding.ASCII.GetBytes(
            "GET /plaintext HTTP/1.1\r\nHost: localhost\r\nAccept: text/plain\r\n\r\n");

        var responseFeatures = CreatePlaintextResponse();

        if (options.WarmupSeconds > 0)
        {
            RunH11Phase(sm, ops, request, responseFeatures, TimeSpan.FromSeconds(options.WarmupSeconds));
            Console.WriteLine("Warmup complete.");
        }

        var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        var (count, elapsed) = RunH11Phase(sm, ops, request, responseFeatures,
            TimeSpan.FromSeconds(options.DurationSeconds));

        var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
        var gcAfter = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var allocDelta = allocAfter - allocBefore;
        var bPerReq = count == 0 ? 0 : (double)allocDelta / count;

        var rps = count / elapsed.TotalSeconds;
        Console.WriteLine();
        Console.WriteLine($"{"Requests",-16}{"RPS",14}{"B/req",14}{"GC 0/1/2",16}");
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"{count,-16}{rps,14:N0}{bPerReq,14:N1}{gcAfter.Item1 - gcBefore.Item1,6}/{gcAfter.Item2 - gcBefore.Item2}/{gcAfter.Item3 - gcBefore.Item3}");
    }

    private static (long Count, TimeSpan Elapsed) RunH11Phase(
        Http11ServerStateMachine sm,
        BenchmarkServerOps ops,
        byte[] requestBytes,
        IFeatureCollection responseFeatures,
        TimeSpan duration)
    {
        long count = 0;
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < deadline)
        {
            for (var batch = 0; batch < 1000; batch++)
            {
                var buffer = TransportBuffer.Rent(requestBytes.Length);
                requestBytes.CopyTo(buffer.FullMemory.Span);
                buffer.Length = requestBytes.Length;
                sm.DecodeClientData(TransportData.Rent(buffer));

                if (ops.Requests.Count > 0)
                {
                    sm.OnResponse(responseFeatures);
                    ops.Clear();
                }

                count++;
            }
        }

        var elapsed = TimeSpan.FromSeconds(
            (double)(Stopwatch.GetTimestamp() - deadline + (long)(duration.TotalSeconds * Stopwatch.Frequency))
            / Stopwatch.Frequency);

        return (count, elapsed);
    }

    private static void RunH2(LoadTestOptions options)
    {
        var serverOptions = new TurboServerOptions();
        serverOptions.Http2.MaxConcurrentStreams = 512;
        var ops = new BenchmarkServerOps();
        var h2Options = serverOptions.ToHttp2Options();
        var sm = new Http2ServerSessionManager(h2Options, ops);

        var hpack = new HpackEncoder(useHuffman: true);
        var preface = Http2Preface();
        var settingsAck = Http2SettingsAck();

        var responseFeatures = CreatePlaintextResponse();

        if (options.WarmupSeconds > 0)
        {
            RunH2Phase(sm, ops, hpack, preface, settingsAck, responseFeatures,
                TimeSpan.FromSeconds(options.WarmupSeconds));
            Console.WriteLine("Warmup complete.");
        }

        var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        var (count, elapsed) = RunH2Phase(sm, ops, hpack, preface, settingsAck, responseFeatures,
            TimeSpan.FromSeconds(options.DurationSeconds));

        var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
        var gcAfter = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var allocDelta = allocAfter - allocBefore;
        var bPerReq = count == 0 ? 0 : (double)allocDelta / count;

        var rps = count / elapsed.TotalSeconds;
        Console.WriteLine();
        Console.WriteLine($"{"Requests",-16}{"RPS",14}{"B/req",14}{"GC 0/1/2",16}");
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"{count,-16}{rps,14:N0}{bPerReq,14:N1}{gcAfter.Item1 - gcBefore.Item1,6}/{gcAfter.Item2 - gcBefore.Item2}/{gcAfter.Item3 - gcBefore.Item3}");
    }

    private static (long Count, TimeSpan Elapsed) RunH2Phase(
        Http2ServerSessionManager sm,
        BenchmarkServerOps ops,
        HpackEncoder hpack,
        byte[] preface,
        byte[] settingsAck,
        IFeatureCollection responseFeatures,
        TimeSpan duration)
    {
        FeedBytes(sm, ops, preface);
        FeedBytes(sm, ops, settingsAck);
        ops.Clear();

        long count = 0;
        var streamId = 1;
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < deadline)
        {
            for (var batch = 0; batch < 1000; batch++)
            {
                var headersFrame = BuildH2HeadersFrame(hpack, streamId);
                FeedBytes(sm, ops, headersFrame);

                if (ops.Requests.Count > 0)
                {
                    var features = ops.Requests[0];
                    features.Set<IHttpResponseFeature>(responseFeatures.Get<IHttpResponseFeature>());
                    features.Set<IHttpResponseBodyFeature>(responseFeatures.Get<IHttpResponseBodyFeature>());
                    sm.OnResponse(features);
                    ops.Clear();
                }

                streamId += 2;
                if (streamId > 0x7FFFFFFF)
                {
                    streamId = 1;
                }

                count++;
            }
        }

        var elapsed = TimeSpan.FromSeconds(
            (double)(Stopwatch.GetTimestamp() - deadline + (long)(duration.TotalSeconds * Stopwatch.Frequency))
            / Stopwatch.Frequency);

        return (count, elapsed);
    }

    private static void FeedBytes(Http2ServerSessionManager sm, BenchmarkServerOps ops, byte[] data)
    {
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(buffer);
    }

    private static byte[] BuildH2HeadersFrame(HpackEncoder hpack, int streamId)
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/plaintext"),
            new(":scheme", "http"),
            new(":authority", "localhost"),
        };

        var hpackBuf = new byte[256];
        var span = hpackBuf.AsSpan();
        var written = hpack.Encode(headers, ref span, useHuffman: true);

        var frameLength = written;
        var frame = new byte[9 + frameLength];
        frame[0] = (byte)(frameLength >> 16);
        frame[1] = (byte)(frameLength >> 8);
        frame[2] = (byte)frameLength;
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x05; // END_STREAM | END_HEADERS
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        hpackBuf.AsSpan(0, written).CopyTo(frame.AsSpan(9));
        return frame;
    }

    private static byte[] Http2Preface()
    {
        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        var settings = new byte[9];
        settings[3] = 0x04; // SETTINGS
        return [.. preface, .. settings];
    }

    private static byte[] Http2SettingsAck()
    {
        var frame = new byte[9];
        frame[3] = 0x04; // SETTINGS
        frame[4] = 0x01; // ACK
        return frame;
    }

    private static IFeatureCollection CreatePlaintextResponse()
    {
        var features = new TurboFeatureCollection();
        var requestFeature = new TurboHttpRequestFeature();
        features.Set<IHttpRequestFeature>(requestFeature);

        var responseFeature = new TurboHttpResponseFeature { StatusCode = 200 };
        responseFeature.Headers["Content-Type"] = "text/plain";
        responseFeature.Headers["Content-Length"] = "13";
        features.Set<IHttpResponseFeature>(responseFeature);

        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        return features;
    }

    private sealed class BenchmarkServerOps : IServerStageOperations
    {
        public List<IFeatureCollection> Requests { get; } = new(16);

        public void OnRequest(IFeatureCollection features) => Requests.Add(features);

        public void OnOutbound(ITransportOutbound item)
        {
            if (item is TransportData td)
            {
                td.Buffer.Dispose();
                td.Return();
            }
        }

        public void OnResponseBodyComplete(IFeatureCollection features) { }

        public void OnScheduleTimer(string name, TimeSpan delay) { }

        public void OnCancelTimer(string name) { }

        public Akka.Event.ILoggingAdapter Log => Akka.Event.NoLogger.Instance;

        public Akka.Actor.IActorRef StageActor { get; set; } = Akka.Actor.ActorRefs.Nobody;

        public Akka.Streams.IMaterializer Materializer { get; set; } = null!;

        public ConnectionPoolContext? PoolContext { get; } = new();

        public void Clear()
        {
            Requests.Clear();
        }
    }
}
