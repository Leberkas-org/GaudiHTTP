using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Servus.Akka.IO.Quic;

namespace Servus.Akka.IO;

public sealed class ClientState : IDisposable
{
    private static readonly PipeOptions InboundPipeOptions = new(
        pool: MemoryPool<byte>.Shared,
        minimumSegmentSize: 4096,
        pauseWriterThreshold: 0,
        resumeWriterThreshold: 0,
        useSynchronizationContext: false);

    private static readonly PipeOptions OutboundPipeOptions = new(
        pool: MemoryPool<byte>.Shared,
        minimumSegmentSize: 4096,
        pauseWriterThreshold: 1024 * 1024,
        resumeWriterThreshold: 512 * 1024,
        useSynchronizationContext: false);

    private static readonly UnboundedChannelOptions ChannelOptions = new()
    {
        SingleReader = true,
        SingleWriter = true
    };

    public Stream Stream { get; }
    public StreamDirection Direction { get; }
    public Action? OnWritesComplete { get; init; }

    public Pipe InboundPipe { get; }
    public Pipe OutboundPipe { get; }

    private readonly Channel<NetworkBuffer> _inboundChannel;
    private readonly Channel<NetworkBuffer> _outboundChannel;

    public ChannelReader<NetworkBuffer> InboundReader => _inboundChannel.Reader;
    public ChannelWriter<NetworkBuffer> InboundWriter => _inboundChannel.Writer;

    public ChannelReader<NetworkBuffer> OutboundReader => _outboundChannel.Reader;
    public ChannelWriter<NetworkBuffer> OutboundWriter => _outboundChannel.Writer;

    public ClientState(Stream stream, StreamDirection direction = StreamDirection.Bidirectional)
    {
        Stream = stream;
        Direction = direction;
        InboundPipe = new Pipe(InboundPipeOptions);
        OutboundPipe = new Pipe(OutboundPipeOptions);
        _inboundChannel = Channel.CreateUnbounded<NetworkBuffer>(ChannelOptions);
        _outboundChannel = Channel.CreateUnbounded<NetworkBuffer>(ChannelOptions);
    }

    public void Dispose()
    {
        _inboundChannel.Writer.TryComplete();
        _outboundChannel.Writer.TryComplete();

        while (_inboundChannel.Reader.TryRead(out var buf)) { buf.Dispose(); }
        while (_outboundChannel.Reader.TryRead(out var buf)) { buf.Dispose(); }

        try { InboundPipe.Writer.Complete(); }
        catch (InvalidOperationException) { _ = 0; }
        try { InboundPipe.Reader.Complete(); }
        catch (InvalidOperationException) { _ = 0; }
        try { OutboundPipe.Writer.Complete(); }
        catch (InvalidOperationException) { _ = 0; }
        try { OutboundPipe.Reader.Complete(); }
        catch (InvalidOperationException) { _ = 0; }

        Stream.Dispose();
    }
}
