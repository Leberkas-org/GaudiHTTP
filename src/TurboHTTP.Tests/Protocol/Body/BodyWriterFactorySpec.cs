using System.Net;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BodyWriterFactorySpec
{
    private static readonly BodyEncoderOptions DefaultOptions = new() { ChunkSize = 16 * 1024 };

    [Fact(Timeout = 5000)]
    public void Create_should_return_null_when_no_body()
    {
        var (writer, encoder) = BodyWriterFactory.Create(
            hasBody: false, contentLength: null,
            httpVersion: HttpVersion.Version11, options: DefaultOptions);

        Assert.Null(writer);
        Assert.Null(encoder);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_streaming_writer_with_passthrough_for_known_length()
    {
        var (writer, encoder) = BodyWriterFactory.Create(
            hasBody: true, contentLength: 1024,
            httpVersion: HttpVersion.Version11, options: DefaultOptions);

        Assert.NotNull(writer);
        Assert.IsType<StreamingBodyWriter>(writer);
        Assert.NotNull(encoder);
        Assert.IsType<PassthroughFramingEncoder>(encoder);
        writer.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_streaming_writer_with_chunked_for_unknown_length()
    {
        var (writer, encoder) = BodyWriterFactory.Create(
            hasBody: true, contentLength: null,
            httpVersion: HttpVersion.Version11, options: DefaultOptions);

        Assert.NotNull(writer);
        Assert.IsType<StreamingBodyWriter>(writer);
        Assert.NotNull(encoder);
        Assert.IsType<ChunkedFramingEncoder>(encoder);
        writer.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_streaming_writer_for_http10_with_known_length()
    {
        var (writer, encoder) = BodyWriterFactory.Create(
            hasBody: true, contentLength: 100,
            httpVersion: HttpVersion.Version10, options: DefaultOptions);

        Assert.NotNull(writer);
        Assert.IsType<StreamingBodyWriter>(writer);
        Assert.NotNull(encoder);
        Assert.IsType<PassthroughFramingEncoder>(encoder);
        writer.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_buffered_writer_for_http10_with_unknown_length()
    {
        var (writer, encoder) = BodyWriterFactory.Create(
            hasBody: true, contentLength: null,
            httpVersion: HttpVersion.Version10, options: DefaultOptions);

        Assert.NotNull(writer);
        Assert.IsType<BufferedBodyWriter>(writer);
        Assert.Null(encoder);
        writer.Dispose();
    }
}
