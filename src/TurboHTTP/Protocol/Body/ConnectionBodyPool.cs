using System.Buffers;
using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Body;

internal sealed class ConnectionBodyPool : IDisposable
{
    private readonly BufferedBodyReader _bufferedReader = new();
    private readonly QueuedBodyReader _queuedReader = new(capacity: 4);
    private readonly ContentLengthFramingDecoder _contentLengthDecoder = new();
    private readonly ChunkedFramingDecoder _chunkedDecoder = new();
    private readonly CloseDelimitedFramingDecoder _closeDelimitedDecoder = new();
    private readonly BufferedBodyWriter _bufferedWriter = new();
    private readonly StreamingBodyWriter _streamingWriter = new();

    public (IBodyReader? Reader, IFramingDecoder? Decoder) RentReader(
        BodyClassification classification, BodyDecoderOptions options)
    {
        switch (classification.Framing)
        {
            case BodyFraming.None:
                return (null, null);

            case BodyFraming.Length:
            {
                var n = classification.ContentLength ?? 0;
                if (n <= options.StreamingThreshold && n <= options.MaxBufferedBodySize)
                {
                    _bufferedReader.Reset((int)n);
                    return (_bufferedReader, null);
                }

                _queuedReader.Reset();
                _contentLengthDecoder.Reset(n);
                return (_queuedReader, _contentLengthDecoder);
            }

            case BodyFraming.Chunked:
            {
                _queuedReader.Reset();
                _chunkedDecoder.Reset(
                    options.MaxStreamedBodySize ?? long.MaxValue,
                    options.MaxChunkExtensionLength);
                return (_queuedReader, _chunkedDecoder);
            }

            case BodyFraming.Close:
            {
                _queuedReader.Reset();
                _closeDelimitedDecoder.Reset(options.MaxStreamedBodySize ?? long.MaxValue);
                return (_queuedReader, _closeDelimitedDecoder);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(classification));
        }
    }

    public void ReturnReader()
    {
    }

    public (IBodyWriter? Writer, IFramingEncoder? Encoder) RentWriter(
        bool hasBody, long? contentLength, Version httpVersion, BodyEncoderOptions options,
        Func<IMemoryOwner<byte>, ReadOnlyMemory<byte>, ValueTask> send,
        Action<IMemoryOwner<byte>, int>? onBufferedComplete = null)
    {
        if (!hasBody)
        {
            return (null, null);
        }

        if (contentLength is not null)
        {
            var encoder = new PassthroughFramingEncoder();
            _streamingWriter.Reset(encoder, send);
            return (_streamingWriter, encoder);
        }

        if (httpVersion == HttpVersion.Version10)
        {
            _bufferedWriter.Reset(onBufferedComplete!);
            return (_bufferedWriter, null);
        }

        var framingEncoder = new ChunkedFramingEncoder(options.ChunkSize);
        _streamingWriter.Reset(framingEncoder, send);
        return (_streamingWriter, framingEncoder);
    }

    public void Dispose()
    {
        _bufferedReader.Dispose();
        _queuedReader.Dispose();
        _bufferedWriter.Dispose();
        _streamingWriter.Dispose();
    }
}