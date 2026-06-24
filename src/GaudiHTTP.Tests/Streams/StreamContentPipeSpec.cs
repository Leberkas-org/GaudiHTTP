using System.IO.Pipelines;
using System.Text;
using GaudiHTTP.Protocol.Syntax;

namespace GaudiHTTP.Tests.Streams;

public sealed class StreamContentPipeSpec
{
    [Fact(Timeout = 5000)]
    public async Task StreamContent_should_read_from_completed_pipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("hello world"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());
        var stream = await content.ReadAsStreamAsync(ct);

        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, ct);

        Assert.True(bytesRead > 0);
        Assert.Equal("hello world", Encoding.UTF8.GetString(buffer, 0, bytesRead));

        bytesRead = await stream.ReadAsync(buffer, ct);
        Assert.Equal(0, bytesRead);
    }

    [Fact(Timeout = 5000)]
    public async Task StreamContent_should_have_null_content_length_for_pipe_stream()
    {
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());
        Assert.Null(content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    public async Task StreamContent_should_read_multiple_chunks_from_pipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("chunk1"u8.ToArray(), ct);
        await writerStream.WriteAsync("chunk2"u8.ToArray(), ct);
        await writerStream.WriteAsync("chunk3"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());
        var body = await content.ReadAsStringAsync(ct);

        Assert.Equal("chunk1chunk2chunk3", body);
    }

    [Fact(Timeout = 5000)]
    public async Task Encoder_drain_pattern_should_work_with_pipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("response body data"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());

        var chunks = new List<string>();
        var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[8];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, ct);
            if (bytesRead == 0)
            {
                break;
            }

            chunks.Add(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        Assert.True(chunks.Count > 1);
        Assert.Equal("response body data", string.Concat(chunks));
    }

    [Fact(Timeout = 5000)]
    public async Task Iterating_content_headers_should_not_block()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("header-test"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.TryAddWithoutValidation("X-Custom", "value");

        var headers = new List<string>();
        foreach (var h in content.Headers)
        {
            headers.Add(h.Key);
        }

        Assert.Contains("X-Custom", headers);
    }

    [Fact(Timeout = 5000)]
    public async Task Full_encoder_simulation_should_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("full sim"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Content = new StreamContent(pipe.Reader.AsStream());
        response.Headers.TransferEncodingChunked = true;

        foreach (var _ in response.Content.Headers)
        {
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, ct);

        Assert.True(bytesRead > 0);
        Assert.Equal("full sim", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsStreamAsync_after_GetHeaderCollection_should_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("after headers"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Content = new StreamContent(pipe.Reader.AsStream());
        response.Headers.TransferEncodingChunked = true;
        response.Headers.TryAddWithoutValidation("X-Custom", "value");

        var headers = response.GetHeaderCollection();
        Assert.True(headers.Count > 0);

        var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, ct);

        Assert.Equal("after headers", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsStreamAsync_after_BodyEncoderFactory_should_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("factory test"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());

        var contentLength = content.Headers.ContentLength;
        Assert.Null(contentLength);

        var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, ct);

        Assert.True(bytesRead > 0, $"Expected data but got 0 bytes. ContentLength was {contentLength}");
        Assert.Equal("factory test", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    [Fact(Timeout = 5000)]
    public async Task Encoder_drain_pattern_on_threadpool_should_work_with_pipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        var writerStream = pipe.Writer.AsStream();

        await writerStream.WriteAsync("threaded data"u8.ToArray(), ct);
        await pipe.Writer.CompleteAsync();

        var content = new StreamContent(pipe.Reader.AsStream());

        var result = await Task.Run(async () =>
        {
            var stream = await content.ReadAsStreamAsync(ct);
            var buffer = new byte[4 * 1024];
            var ms = new MemoryStream();
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct);
                if (bytesRead == 0)
                {
                    break;
                }

                ms.Write(buffer, 0, bytesRead);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }, ct);

        Assert.Equal("threaded data", result);
    }
}