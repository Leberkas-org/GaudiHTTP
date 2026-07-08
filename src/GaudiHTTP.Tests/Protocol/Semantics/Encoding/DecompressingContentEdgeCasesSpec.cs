using System.IO.Compression;
using System.Net.Http;
using GaudiHTTP.Internal;

namespace GaudiHTTP.Tests.Protocol.Semantics.Encoding;

public sealed class DecompressingContentEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_decompress_deflate()
    {
        var original = "deflate test data"u8.ToArray();
        var compressed = ZLibCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "deflate");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_decompress_deflate()
    {
        var original = "deflate sync test"u8.ToArray();
        var compressed = ZLibCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "deflate");

        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_throw_on_corrupt_gzip()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF };
        var inner = new ByteArrayContent(corrupt);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();

        // A corrupt/truncated compressed body must surface as an error, not silently truncate.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            content.CopyToAsync(ms, TestContext.Current.CancellationToken));
        Assert.NotNull(ex.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_throw_on_corrupt_gzip()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF };
        var inner = new ByteArrayContent(corrupt);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();

        var ex = Assert.Throws<HttpRequestException>(() => content.CopyTo(ms, null, CancellationToken.None));
        Assert.NotNull(ex.InnerException);
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_decompress_brotli()
    {
        var original = "brotli test data"u8.ToArray();
        var compressed = BrotliCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "br");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, TestContext.Current.CancellationToken);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void SerializeToStream_should_decompress_brotli()
    {
        var original = "brotli sync test"u8.ToArray();
        var compressed = BrotliCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "br");

        using var ms = new MemoryStream();
        content.CopyTo(ms, null, CancellationToken.None);

        Assert.Equal(original, ms.ToArray());
    }

    private static byte[] ZLibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zl = new ZLibStream(ms, CompressionLevel.Fastest))
        {
            zl.Write(data);
        }

        return ms.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var br = new BrotliStream(ms, CompressionLevel.Fastest))
        {
            br.Write(data);
        }

        return ms.ToArray();
    }
}