using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.End2End.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class ConnectionWindowStarvationSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureClientOptions(TurboClientOptions options)
    {
        options.Http2.MaxConnectionsPerServer = 1;
    }

    protected override void ConfigureServer(TurboServerOptions options, ushort port, X509Certificate2? cert)
    {
        base.ConfigureServer(options, port, cert);
        options.Http2.InitialConnectionWindowSize = 512 * 1024;
        options.Http2.InitialStreamWindowSize = 128 * 1024;
        options.Limits.MinRequestBodyDataRate = 0;
        options.Limits.MinResponseDataRate = 0;
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapPost("/echo-bytes", async ctx =>
        {
            using var stream = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(stream, ctx.RequestAborted);
            var data = stream.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(data, ctx.RequestAborted);
        });
    }

    [Fact(Timeout = 60000)]
    public async Task ConnectionWindowStarvation_should_complete_all_streams_with_small_connection_window()
    {
        const int concurrentRequests = 10;
        const int payloadSize = 128 * 1024;
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

    [Fact(Timeout = 60000)]
    public async Task ConnectionWindowStarvation_should_distribute_bandwidth_across_streams()
    {
        const int concurrentRequests = 8;
        const int payloadSize = 256 * 1024;
        var payloads = new byte[concurrentRequests][];
        var completionOrder = new List<int>();

        for (var i = 0; i < concurrentRequests; i++)
        {
            payloads[i] = new byte[payloadSize];
            RandomNumberGenerator.Fill(payloads[i]);
        }

        var tasks = new Task<(int index, bool success)>[concurrentRequests];

        for (var i = 0; i < concurrentRequests; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
                {
                    Content = new ByteArrayContent(payloads[index])
                };

                var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return (index, false);
                }

                var responseBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

                lock (completionOrder)
                {
                    completionOrder.Add(index);
                }

                return (index, payloads[index].SequenceEqual(responseBytes));
            });
        }

        var results = await Task.WhenAll(tasks);

        Assert.True(results.All(r => r.success), "All streams must complete successfully");
        Assert.Equal(concurrentRequests, completionOrder.Count);
    }
}
