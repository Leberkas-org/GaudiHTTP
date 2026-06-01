using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.End2End.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class DataRateEnforcementSpec : End2EndSpecBase
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

        app.MapGet("/generate", async ctx =>
        {
            var size = int.Parse(ctx.Request.Query["size"]!);
            ctx.Response.ContentType = "application/octet-stream";
            var buffer = new byte[16 * 1024];
            Array.Fill(buffer, (byte)0xAB);
            var remaining = size;
            while (remaining > 0)
            {
                var toWrite = Math.Min(buffer.Length, remaining);
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, toWrite), CancellationToken);
                remaining -= toWrite;
            }
        });
    }

    [Fact(Timeout = 30000)]
    public async Task DataRateEnforcement_should_not_kill_streams_under_normal_concurrent_load()
    {
        const int concurrentRequests = 10;
        const int payloadSize = 256 * 1024;
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
                    return responseBytes.Length == payloads[index].Length && payloads[index].SequenceEqual(responseBytes)
                        ? (index, true, "")
                        : (index, false, "Payload mismatch");
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
    public async Task DataRateEnforcement_should_reset_stream_when_client_sends_below_minimum_rate()
    {
        var payload = new byte[64 * 1024];
        RandomNumberGenerator.Fill(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
        {
            Content = new StreamContent(new ThrottledStream(payload, bytesPerChunk: 32, delayPerChunk: TimeSpan.FromMilliseconds(200)))
        };
        request.Content.Headers.ContentLength = payload.Length;

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await Client.SendAsync(request, CancellationToken);
            await response.Content.ReadAsByteArrayAsync(CancellationToken);
        });

        Assert.True(
            ex is HttpRequestException or OperationCanceledException,
            $"Expected HttpRequestException or OperationCanceledException, got {ex.GetType().Name}: {ex.Message}");
    }

    [Fact(Timeout = 30000)]
    public async Task DataRateEnforcement_should_not_affect_fast_streams_when_slow_stream_is_killed()
    {
        var fastPayload = new byte[128 * 1024];
        RandomNumberGenerator.Fill(fastPayload);
        var slowPayload = new byte[64 * 1024];
        RandomNumberGenerator.Fill(slowPayload);

        var slowTask = Task.Run(async () =>
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
                {
                    Content = new StreamContent(new ThrottledStream(slowPayload, bytesPerChunk: 32, delayPerChunk: TimeSpan.FromMilliseconds(200)))
                };
                request.Content.Headers.ContentLength = slowPayload.Length;

                var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
                await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                return (success: false, error: "Expected slow request to be reset");
            }
            catch
            {
                return (success: true, error: "");
            }
        });

        await Task.Delay(100, CancellationToken);

        var fastTasks = new Task<bool>[5];
        for (var i = 0; i < fastTasks.Length; i++)
        {
            fastTasks[i] = Task.Run(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUri}/echo-bytes")
                {
                    Content = new ByteArrayContent(fastPayload)
                };

                var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return false;
                }

                var responseBytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                return fastPayload.SequenceEqual(responseBytes);
            });
        }

        var fastResults = await Task.WhenAll(fastTasks);
        Assert.True(fastResults.All(r => r), "Fast streams should not be affected by slow stream enforcement");

        var slowResult = await slowTask;
        Assert.True(slowResult.success, slowResult.error);
    }

    private sealed class ThrottledStream(byte[] data, int bytesPerChunk, TimeSpan delayPerChunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length)
            {
                return 0;
            }

            await Task.Delay(delayPerChunk, cancellationToken);

            var toRead = Math.Min(bytesPerChunk, Math.Min(buffer.Length, data.Length - _position));
            data.AsMemory(_position, toRead).CopyTo(buffer);
            _position += toRead;
            return toRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var toRead = Math.Min(count, data.Length - _position);
            if (toRead <= 0)
            {
                return 0;
            }

            Array.Copy(data, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
