using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H2;

/// <summary>
/// Regression spec for the QueuedBodyReader cross-thread race that intermittently lost,
/// duplicated, or reordered whole 16KB DATA frames under concurrent multiplexed streams.
/// Payload bytes encode (stream, 16KB-block), so any integrity failure reports exactly
/// which blocks were lost/reordered and on which side (request vs response) it happened.
/// Runs over h2c so the TLS layer is out of the picture.
/// </summary>
[Collection("H2")]
public sealed class PatternedPayloadIntegritySpec : End2EndSpecBase
{
    private const int BlockSize = 16 * 1024;

    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override bool UseTls => false;

    protected override TimeSpan ClientTimeout => TimeSpan.FromSeconds(45);

    protected override void ConfigureServer(TurboServerOptions options, ushort port, System.Security.Cryptography.X509Certificates.X509Certificate2? cert)
    {
        options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapPost("/echo-bytes", async ctx =>
        {
            using var stream = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(stream, ctx.RequestAborted);
            var data = stream.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.Headers["X-Received-Length"] = data.Length.ToString();
            ctx.Response.Headers["X-Request-Analysis"] = Analyze(data);
            await ctx.Response.Body.WriteAsync(data, ctx.RequestAborted);
        });
    }

    private static byte[] BuildPayload(int stream, int size)
    {
        var data = new byte[size];
        for (var p = 0; p < size; p++)
        {
            var block = p / BlockSize;
            data[p] = (p % 4) switch
            {
                0 => 0xA0,
                1 => (byte)stream,
                2 => (byte)block,
                _ => (byte)(block >> 8)
            };
        }

        return data;
    }

    // Walks 4-byte words and summarizes the observed (stream, block) sequence as runs.
    private static string Analyze(byte[] data)
    {
        if (data.Length % 4 != 0)
        {
            return $"len={data.Length} (not word aligned)";
        }

        var sb = new StringBuilder();
        var runStream = -1;
        var runStartBlock = -1;
        var runEndBlock = -1;

        void FlushRun()
        {
            if (runStream < 0)
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append($"s{runStream}:b{runStartBlock}");
            if (runEndBlock != runStartBlock)
            {
                sb.Append($"-{runEndBlock}");
            }
        }

        for (var p = 0; p < data.Length; p += 4)
        {
            if (data[p] != 0xA0)
            {
                FlushRun();
                runStream = -1;
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append($"GARBAGE@{p}");
                while (p + 4 < data.Length && data[p + 4] != 0xA0)
                {
                    p += 4;
                }

                continue;
            }

            var s = data[p + 1];
            var b = data[p + 2] | (data[p + 3] << 8);

            if (s == runStream && (b == runEndBlock || b == runEndBlock + 1))
            {
                runEndBlock = b;
            }
            else
            {
                FlushRun();
                runStream = s;
                runStartBlock = b;
                runEndBlock = b;
            }
        }

        FlushRun();
        return $"len={data.Length} [{sb}]";
    }

    [Fact(Timeout = 60000)]
    public async Task Http2_should_roundtrip_concurrent_patterned_payloads_exactly()
    {
        const int concurrentRequests = 20;
        const int payloadSize = 512 * 1024;

        var tasks = Enumerable.Range(0, concurrentRequests).Select(index => Task.Run(async () =>
        {
            var payload = BuildPayload(index, payloadSize);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
            {
                Content = new ByteArrayContent(payload)
            };

            var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return $"[{index}] status {response.StatusCode}";
            }

            var responseBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            if (payload.SequenceEqual(responseBytes))
            {
                return null;
            }

            var requestAnalysis = response.Headers.TryGetValues("X-Request-Analysis", out var v)
                ? string.Join(",", v)
                : "?";
            return $"[{index}] RESPONSE {Analyze(responseBytes)} || REQUEST-AT-SERVER {requestAnalysis}";
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        var failures = results.Where(r => r is not null).ToArray();
        Assert.True(failures.Length == 0, string.Join("\n", failures));
    }
}
