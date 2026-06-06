using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

public sealed class Http2ServerSettingsSpec
{
    private static Http2ServerEncoderOptions DefaultEncoderOptions() => new()
    {
        MaxFrameSize = 16 * 1024,
        HeaderTableSize = 4096,
        WriteDateHeader = false,
        MaxHeaderBytes = 32 * 1024,
        UseHuffman = true
    };

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void ApplyClientSettings_updates_max_frame_size()
    {
        var encoder = new Http2ServerEncoder(DefaultEncoderOptions());

        // Verify default max frame size
        Assert.Equal(16384, encoder.MaxFrameSize);

        // Apply larger max frame size
        var settings = new[] { (SettingsParameter.MaxFrameSize, (uint)32768) };
        encoder.ApplyClientSettings(settings);

        Assert.Equal(32768, encoder.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void ApplyClientSettings_updates_header_table_size()
    {
        var encoder = new Http2ServerEncoder(DefaultEncoderOptions());

        var settings = new[] { (SettingsParameter.HeaderTableSize, (uint)8192) };
        encoder.ApplyClientSettings(settings);

        // Verify settings applied without exception
        var ctx = ServerTestContext.CreateResponse();
        ctx.Get<IHttpResponseFeature>()?.Headers["x-test"] = "value";

        var frames = encoder.EncodeHeaders(ctx, streamId: 1, hasBody: false);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Default_max_frame_size_is_16384()
    {
        var encoder = new Http2ServerEncoder(DefaultEncoderOptions());

        Assert.Equal(16384, encoder.MaxFrameSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void ResetHpack_allows_encoder_reuse()
    {
        var encoder = new Http2ServerEncoder(DefaultEncoderOptions());

        var ctx1 = ServerTestContext.CreateResponse();
        ctx1.Get<IHttpResponseFeature>()?.Headers["x-header"] = "value1";

        var frames1 = encoder.EncodeHeaders(ctx1, streamId: 1, hasBody: false);
        Assert.NotEmpty(frames1);

        encoder.ResetHpack();

        var ctx2 = ServerTestContext.CreateResponse();
        ctx2.Get<IHttpResponseFeature>()?.Headers["x-header"] = "value2";

        var frames2 = encoder.EncodeHeaders(ctx2, streamId: 3, hasBody: false);
        Assert.NotEmpty(frames2);
    }
}
