using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class DefaultSettingsSmokeSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapPost("/echo-bytes", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, CancellationToken);
            var data = ms.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(data, CancellationToken);
        });

        app.MapGet("/generate", async ctx =>
        {
            var size = int.Parse(ctx.Request.Query["size"]!);
            ctx.Response.ContentType = "application/octet-stream";
            var buffer = new byte[64 * 1024];
            Array.Fill(buffer, (byte)0xCD);
            var remaining = size;
            while (remaining > 0)
            {
                var toWrite = Math.Min(buffer.Length, remaining);
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, toWrite), CancellationToken);
                remaining -= toWrite;
            }
        });
    }

    [Fact(Timeout = 60000)]
    public async Task Defaults_should_handle_concurrent_POST_echo_without_rate_violations()
    {
        const int concurrentRequests = 10;
        const int payloadSize = 512 * 1024;
        var payloads = new byte[concurrentRequests][];

        for (var i = 0; i < concurrentRequests; i++)
        {
            payloads[i] = new byte[payloadSize];
            RandomNumberGenerator.Fill(payloads[i]);
        }

        var tasks = new Task<(int index, bool success, string error)>[concurrentRequests];

        for (var i = 0; i < concurrentRequests; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
                    {
                        Content = new ByteArrayContent(payloads[index])
                    };

                    var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return (index, false, $"Status: {response.StatusCode}");
                    }

                    var responseBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                    return payloads[index].SequenceEqual(responseBytes)
                        ? (index, true, "")
                        : (index, false, $"Payload mismatch (got {responseBytes.Length} bytes)");
                }
                catch (Exception ex)
                {
                    return (index, false, ex.Message);
                }
            });
        }

        var results = await Task.WhenAll(tasks);

        var failures = results.Where(r => !r.success).ToArray();
        Assert.Empty(failures);
    }

    [Fact(Timeout = 30000)]
    public async Task Defaults_should_stream_large_response_with_adaptive_scaling()
    {
        const int responseSize = 4 * 1024 * 1024;

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/generate?size={responseSize}");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(responseSize, body.Length);
        Assert.True(body.All(b => b == 0xCD));
    }

    [Fact(Timeout = 60000)]
    public async Task Defaults_should_handle_concurrent_large_responses_with_data_rate_active()
    {
        const int concurrentRequests = 5;
        const int responseSize = 2 * 1024 * 1024;

        var tasks = new Task<(bool success, int length)>[concurrentRequests];

        for (var i = 0; i < concurrentRequests; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/generate?size={responseSize}");
                var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return (false, 0);
                }

                var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                return body.Length == responseSize && body.All(b => b == 0xCD)
                    ? (true, body.Length)
                    : (false, body.Length);
            });
        }

        var results = await Task.WhenAll(tasks);

        Assert.True(results.All(r => r.success),
            $"Failures: {string.Join(", ", results.Where(r => !r.success).Select(r => $"len={r.length}"))}");
    }
}
