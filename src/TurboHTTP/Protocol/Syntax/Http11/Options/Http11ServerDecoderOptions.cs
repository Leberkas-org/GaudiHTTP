using System.Buffers;

namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ServerDecoderOptions
{
    public required int MaxPipelinedRequests { get; init; }
    public required int MaxChunkExtensionLength { get; init; }
    public required long StreamingThreshold { get; init; }
    public required long MaxBufferedBodySize { get; init; }
    public required long? MaxStreamedBodySize { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required int MaxHeaderCount { get; init; }
    public required int HeaderLineMaxLength { get; init; }
    public required int RequestLineMaxLength { get; init; }
    public required int MaxRequestTargetLength { get; init; }
    public required bool AllowObsFold { get; init; }
    public required MemoryPool<byte> BufferPool { get; init; }
}
