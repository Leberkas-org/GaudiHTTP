using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.Settings;

public sealed class Http2SettingsLifecycleSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void FlowController_should_emit_ack_frame_when_remote_settings_received()
    {
        var flow = new FlowController(65535, 65535);
        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 128u)]);

        var result = flow.OnRemoteSettings(settings);

        Assert.NotNull(result.AckFrame);
        Assert.True(result.AckFrame!.IsAck);
        Assert.Empty(result.AckFrame.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void FlowController_should_ignore_ack_frame_when_processing_remote_settings()
    {
        var flow = new FlowController(65535, 65535);
        var ack = new SettingsFrame([], isAck: true);

        var result = flow.OnRemoteSettings(ack);

        Assert.Null(result.AckFrame);
        Assert.Null(result.MaxConcurrentStreamsChange);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void FlowController_should_apply_initial_window_size_when_settings_received()
    {
        var flow = new FlowController(65535, 65535);
        flow.InitStreamSendWindow(1);
        flow.InitStreamSendWindow(3);

        var settings = new SettingsFrame([(SettingsParameter.InitialWindowSize, 32768u)]);
        var result = flow.OnRemoteSettings(settings);

        Assert.Equal(32768, result.InitialWindowSizeChange);
        Assert.Equal(32768, flow.GetSendWindow(1));
        Assert.Equal(32768, flow.GetSendWindow(3));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void FlowController_should_report_max_concurrent_streams_change_when_settings_received()
    {
        var flow = new FlowController(65535, 65535);
        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 50u)]);

        var result = flow.OnRemoteSettings(settings);

        Assert.Equal(50, result.MaxConcurrentStreamsChange);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void HpackDecoder_should_accept_table_size_update_when_settings_header_table_size_changes()
    {
        var decoder = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: false);

        encoder.AcknowledgeTableSizeChange(2048);
        var block = encoder.Encode([(":status", "200"), ("content-type", "text/html")]);

        var headers = decoder.Decode(block.Span);

        Assert.Equal(2, headers.Count);
        Assert.Equal(":status", headers[0].Name);
        Assert.Equal("200", headers[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void HpackDecoder_should_accept_zero_table_size_when_settings_header_table_size_is_zero()
    {
        var decoder = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: false);

        encoder.AcknowledgeTableSizeChange(0);
        var block = encoder.Encode([(":status", "200")]);

        var headers = decoder.Decode(block.Span);

        Assert.Single(headers);
        Assert.Equal("200", headers[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void FrameDecoder_should_decode_without_validating_enable_push_when_settings_has_invalid_value()
    {
        var decoder = new FrameDecoder();
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 3u)]).Serialize();

        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.EnablePush, Item2: 3u });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void FlowController_should_process_multiple_parameters_in_order_when_settings_has_mixed_params()
    {
        var flow = new FlowController(65535, 65535);
        flow.InitStreamSendWindow(1);

        var settings = new SettingsFrame([
            (SettingsParameter.MaxConcurrentStreams, 200u),
            (SettingsParameter.InitialWindowSize, 16384u)
        ]);

        var result = flow.OnRemoteSettings(settings);

        Assert.Equal(200, result.MaxConcurrentStreamsChange);
        Assert.Equal(16384, result.InitialWindowSizeChange);
        Assert.Equal(16384, flow.GetSendWindow(1));
    }
}