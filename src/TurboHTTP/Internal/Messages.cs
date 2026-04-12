using System.Buffers;
using System.Collections.Concurrent;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Internal;

public interface IInputItem
{
    RequestEndpoint Key { get; }
}

public interface IOutputItem
{
    RequestEndpoint Key { get; }
}

public interface IControlItem : IOutputItem;

public readonly record struct ConnectionReuseItem(ConnectionReuseDecision Decision) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct ConnectItem(TcpOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public sealed class NetworkBuffer : IInputItem, IOutputItem
{
    private static readonly ConcurrentStack<NetworkBuffer> WrapperPool = new();

    private static int _maxPoolSize = Environment.ProcessorCount * 2;

    private IMemoryOwner<byte>? _owner;

    public int Length { get; set; }

    public RequestEndpoint Key { get; set; }

    public Memory<byte> Memory => _owner!.Memory[..Length];

    public ReadOnlySpan<byte> Span => _owner!.Memory.Span[..Length];

    internal Memory<byte> FullMemory => _owner!.Memory;

    internal int Capacity => _owner?.Memory.Length ?? 0;

    private NetworkBuffer()
    {
    }

    internal static void ConfigurePoolSize(int maxPoolSize)
    {
        _maxPoolSize = maxPoolSize;
    }

    public static NetworkBuffer Rent(int minimumSize)
    {
        var owner = MemoryPool<byte>.Shared.Rent(minimumSize);
        if (!WrapperPool.TryPop(out var buf))
        {
            return new NetworkBuffer { _owner = owner };
        }

        buf._owner = owner;
        buf.Length = 0;
        buf.Key = default;
        return buf;
    }

    internal static NetworkBuffer FromArray(byte[] data, int length = -1)
    {
        var len = length < 0 ? data.Length : length;
        return new NetworkBuffer { _owner = new NonDisposingOwner(data), Length = len };
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
        {
            return;
        }

        owner.Dispose();

        // Only return to pool if capacity allows
        if (_maxPoolSize > 0 && WrapperPool.Count < _maxPoolSize)
        {
            WrapperPool.Push(this);
        }
    }

    private sealed class NonDisposingOwner(byte[] data) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = data;

        public void Dispose()
        {
        }
    }
}

public readonly record struct MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct StreamAcquireItem : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public enum TlsCloseKind
{
    /// <summary>
    /// The peer sent a TLS close_notify alert before closing the connection,
    /// or a plain TCP connection received a FIN. The response body (if any)
    /// that was buffered before the close is considered complete (RFC 9112 §9.8).
    /// </summary>
    CleanClose,

    /// <summary>
    /// The connection was closed abruptly (TCP RST, I/O error, or TLS error
    /// without close_notify). Any partially received response must be treated
    /// as incomplete and should not be delivered to the application.
    /// </summary>
    AbruptClose
}

public readonly record struct CloseSignalItem(TlsCloseKind CloseKind) : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct ConnectedSignalItem : IInputItem
{
    public RequestEndpoint Key { get; init; }
}

public readonly record struct ReconnectItem : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public enum OutputStreamType
{
    /// <summary>Bidirectional request stream (default for request/response data).</summary>
    Request,

    /// <summary>Unidirectional control stream (type 0x00) — carries SETTINGS and GOAWAY frames.</summary>
    Control,

    /// <summary>Unidirectional QPACK encoder instruction stream (type 0x02).</summary>
    QpackEncoder,
}

/// <summary>
/// Identifies the QUIC unidirectional stream that an inbound HTTP/3 item arrived on.
/// Used to route inbound items to the correct processing pipeline.
/// </summary>
public enum InputStreamType
{
    /// <summary>Bidirectional request/response stream (default).</summary>
    Request,

    /// <summary>Unidirectional control stream (type 0x00) — carries SETTINGS and GOAWAY frames.</summary>
    Control,

    /// <summary>Unidirectional QPACK encoder instruction stream (type 0x02).</summary>
    QpackEncoder,

    /// <summary>Unidirectional QPACK decoder instruction stream (type 0x03).</summary>
    QpackDecoder,
}

/// <summary>
/// Wraps an <see cref="IInputItem"/> with an <see cref="InputStreamType"/> tag
/// so the engine can route it to the correct processing pipeline.
/// </summary>
public readonly record struct Http3InputTaggedItem(IInputItem Inner, InputStreamType StreamType) : IInputItem
{
    public RequestEndpoint Key => Inner.Key;
}

/// <summary>
/// Wraps an <see cref="IOutputItem"/> with an <see cref="OutputStreamType"/> tag
/// so the demux stage can route it to the correct QUIC stream.
/// </summary>
public readonly record struct Http3OutputTaggedItem(IOutputItem Inner, OutputStreamType StreamType) : IOutputItem
{
    public RequestEndpoint Key => Inner.Key;
}