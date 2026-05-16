using System.IO.Pipelines;
using System.Net;

namespace TurboHTTP.Internal;

internal class PipedStreamContent : HttpContent
{
    internal PipeReader Reader { get; }

    public PipedStreamContent(PipeReader reader)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        return Reader.AsStream();
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult(Reader.AsStream());
    }

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Reader.AsStream());
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        Reader.AsStream().CopyTo(stream);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return Reader.CopyToAsync(stream);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        return Reader.AsStream().CopyToAsync(stream, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}