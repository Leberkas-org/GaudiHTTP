using System.IO.Pipelines;
using System.Net;

namespace TurboHTTP.Internal;

internal class PipedStreamContent(PipeReader reader) : HttpContent
{
    private readonly PipeReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        return _reader.AsStream();
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult(_reader.AsStream());
    }

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_reader.AsStream());
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        _reader.AsStream().CopyTo(stream);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return _reader.CopyToAsync(stream);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        return _reader.AsStream().CopyToAsync(stream, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (Headers.ContentLength is null)
        {
            length = 0;
            return false;
        }

        length = Headers.ContentLength.Value;
        return true;
    }
}