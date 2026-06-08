using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class ConcurrentLargePostSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapPost("/echo-bytes", async ctx =>
        {
            using var stream = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(stream, CancellationToken);
            var data = stream.ToArray();
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(data, CancellationToken);
        });
    }

    [Fact(Timeout = 60000)]
    public async Task ConcurrentLargePost_should_handle_concurrent_512KB_payloads_without_corruption()
    {
        const int concurrentRequests = 20;
        const int payloadSize = 512 * 1024;
        var payloads = new byte[concurrentRequests][];

        // Generate unique random payloads
        for (var i = 0; i < concurrentRequests; i++)
        {
            payloads[i] = new byte[payloadSize];
            RandomNumberGenerator.Fill(payloads[i]);
        }

        var tasks = new Task<(int index, bool success, string error)>[concurrentRequests];

        // Fire all requests concurrently on the same client/connection
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

                    if (responseBytes.Length != payloads[index].Length)
                    {
                        return (index, false,
                            $"Length mismatch: expected {payloads[index].Length}, got {responseBytes.Length}");
                    }

                    if (!payloads[index].SequenceEqual(responseBytes))
                    {
                        return (index, false, "Payload mismatch: response body does not match request");
                    }

                    return (index, true, "");
                }
                catch (Exception ex)
                {
                    return (index, false, ex.Message);
                }
            });
        }

        var results = await Task.WhenAll(tasks);

        var failedResults = results.Where(r => !r.success).ToArray();
        Assert.Empty(failedResults);
    }

    [Fact(Timeout = 60000)]
    public async Task ConcurrentLargePost_should_maintain_stream_isolation_under_flow_control()
    {
        const int concurrentRequests = 10;
        const int payloadSize = 1024 * 1024; // 1 MB each
        var payloads = new byte[concurrentRequests][];
        var checksums = new long[concurrentRequests];

        // Generate unique payloads and compute checksums
        for (var i = 0; i < concurrentRequests; i++)
        {
            payloads[i] = new byte[payloadSize];
            RandomNumberGenerator.Fill(payloads[i]);

            // Simple checksum
            long sum = 0;
            foreach (var b in payloads[i])
            {
                sum += b;
            }
            checksums[i] = sum;
        }

        var results = new (int index, long checksum, bool valid)[concurrentRequests];

        var tasks = new Task[concurrentRequests];

        // Fire all requests concurrently
        for (var i = 0; i < concurrentRequests; i++)
        {
            var index = i;
#pragma warning disable xUnit1051
            tasks[i] = Task.Run(async () =>
            {
#pragma warning restore xUnit1051
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
                    {
                        Content = new ByteArrayContent(payloads[index])
                    };

                    var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                    var responseBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

                    // Verify exact match
                    Assert.Equal(payloads[index].Length, responseBytes.Length);
                    Assert.True(payloads[index].SequenceEqual(responseBytes),
                        $"Stream {index}: response body mismatch");

                    // Compute checksum of response
                    long sum = 0;
                    foreach (var b in responseBytes)
                    {
                        sum += b;
                    }

                    results[index] = (index, sum, sum == checksums[index]);
                }
                catch (Exception)
                {
                    results[index] = (index, 0, false);
                    throw;
                }
            });
        }

        await Task.WhenAll(tasks);

        // Verify all checksums match
        var invalidResults = results.Where(r => !r.valid).ToArray();
        Assert.Empty(invalidResults);
    }

    [Fact(Timeout = 90000)]
    public async Task ConcurrentLargePost_should_handle_interleaved_sends_and_receives()
    {
        const int concurrentRequests = 10;
        const int payloadSize = 512 * 1024;
        var payloads = new byte[concurrentRequests][];

        for (var i = 0; i < concurrentRequests; i++)
        {
            payloads[i] = new byte[payloadSize];
            RandomNumberGenerator.Fill(payloads[i]);
        }

        var semaphore = new SemaphoreSlim(5);
        var tasks = new Task<bool>[concurrentRequests];

        for (var i = 0; i < concurrentRequests; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(TestContext.Current.CancellationToken);
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(30));

                    var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
                    {
                        Content = new ByteArrayContent(payloads[index])
                    };

                    var response = await Client.SendAsync(request, cts.Token);

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return false;
                    }

                    var responseBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    return payloads[index].SequenceEqual(responseBytes);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r);

        Assert.True(successCount >= concurrentRequests - 2,
            $"Expected at least {concurrentRequests - 2} successes, got {successCount}/{concurrentRequests}");
    }
}
