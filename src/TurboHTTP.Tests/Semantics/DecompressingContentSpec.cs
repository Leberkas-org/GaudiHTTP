using System.IO.Compression;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Semantics;

public sealed class DecompressingContentSpec
{
    [Fact(Timeout = 5000)]
    public async Task SerializeToStreamAsync_should_decompress_gzip()
    {
        var original = "hello compressed world"u8.ToArray();
        var compressed = GzipCompress(original);

        var inner = new ByteArrayContent(compressed);
        using var content = new DecompressingContent(inner, "gzip");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Serialize_after_dispose_should_throw_ObjectDisposedException()
    {
        var inner = new ByteArrayContent(GzipCompress([1, 2, 3]));
        var content = new DecompressingContent(inner, "gzip");
        content.Dispose();

        using var ms = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => content.CopyTo(ms, null, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task SerializeAsync_after_dispose_should_throw_ObjectDisposedException()
    {
        var inner = new ByteArrayContent(GzipCompress([1, 2, 3]));
        var content = new DecompressingContent(inner, "gzip");
        content.Dispose();

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => content.CopyToAsync(ms));
    }

    [Fact(Timeout = 5000)]
    public void Double_dispose_should_not_throw()
    {
        var inner = new ByteArrayContent(GzipCompress([1, 2, 3]));
        var content = new DecompressingContent(inner, "gzip");
        content.Dispose();
        content.Dispose();
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
        {
            gz.Write(data);
        }

        return ms.ToArray();
    }
}
