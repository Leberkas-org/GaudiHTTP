using System.Buffers;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Server;

internal static class Http1ConnectionOptionsExtensions
{
    public static BodyEncoderOptions ToBodyEncoderOptions(this Http1ConnectionOptions o) => new()
    {
        ChunkSize = o.ResponseBodyChunkSize,
    };

    public static Http10ServerEncoderOptions ToHttp10EncoderOptions(this Http1ConnectionOptions o) => new()
    {
        WriteDateHeader = true,
        MaxHeaderBytes = o.MaxHeaderListSize,
    };

    public static Http10ServerDecoderOptions ToHttp10DecoderOptions(this Http1ConnectionOptions o) => new()
    {
        StreamingThreshold = o.BodyBufferThreshold,
        MaxBufferedBodySize = o.BodyBufferThreshold,
        MaxStreamedBodySize = o.Limits.MaxRequestBodySize,
        MaxHeaderBytes = o.MaxHeaderListSize,
        MaxHeaderCount = o.MaxHeaderCount,
        HeaderLineMaxLength = o.MaxRequestLineLength,
        RequestLineMaxLength = o.MaxRequestLineLength,
        MaxRequestTargetLength = o.MaxRequestTargetLength,
        AllowObsFold = o.AllowObsFold,
        BufferPool = MemoryPool<byte>.Shared,
    };

    public static Http11ServerEncoderOptions ToHttp11EncoderOptions(this Http1ConnectionOptions o) => new()
    {
        KeepAliveTimeout = o.Limits.KeepAliveTimeout,
        RequestHeadersTimeout = o.Limits.RequestHeadersTimeout,
        WriteDateHeader = true,
        MaxHeaderBytes = o.MaxHeaderListSize,
    };

    public static Http11ServerDecoderOptions ToHttp11DecoderOptions(this Http1ConnectionOptions o) => new()
    {
        MaxPipelinedRequests = o.MaxPipelinedRequests,
        MaxChunkExtensionLength = o.MaxChunkExtensionLength,
        StreamingThreshold = o.BodyBufferThreshold,
        MaxBufferedBodySize = o.BodyBufferThreshold,
        MaxStreamedBodySize = o.Limits.MaxRequestBodySize,
        MaxHeaderBytes = o.MaxHeaderListSize,
        MaxHeaderCount = o.MaxHeaderCount,
        HeaderLineMaxLength = o.MaxRequestLineLength,
        RequestLineMaxLength = o.MaxRequestLineLength,
        MaxRequestTargetLength = o.MaxRequestTargetLength,
        AllowObsFold = o.AllowObsFold
    };
}