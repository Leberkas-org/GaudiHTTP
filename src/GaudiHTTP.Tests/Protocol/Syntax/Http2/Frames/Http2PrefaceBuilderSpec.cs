using System.Buffers.Binary;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2PrefaceBuilderSpec
{
    private const int MagicLength = 24;
    private const int FrameHeaderSize = 9;
    private const int SettingSize = 6;
    private const int SettingsCount = 5; // HeaderTableSize, EnablePush, InitialWindowSize, MaxFrameSize, MaxHeaderListSize

    private static ReadOnlySpan<byte> ParseSettings(ReadOnlySpan<byte> preface, out bool hasWindowUpdate)
    {
        var settingsPayload = preface.Slice(MagicLength + FrameHeaderSize, SettingsCount * SettingSize);
        hasWindowUpdate = preface.Length > MagicLength + FrameHeaderSize + SettingsCount * SettingSize;
        return settingsPayload;
    }

    private static (SettingsParameter Key, uint Value) ReadSetting(ReadOnlySpan<byte> span, int index)
    {
        var offset = index * SettingSize;
        var key = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
        var value = BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 2)..]);
        return (key, value);
    }

    private const int DefaultWindow = 65535;
    private const int DefaultHeaderListSize = 64 * 1024;

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_default_header_table_size_4096()
    {
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, DefaultWindow, 4096, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 0);

        Assert.Equal(SettingsParameter.HeaderTableSize, key);
        Assert.Equal(4096u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_custom_header_table_size_when_specified()
    {
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, DefaultWindow, headerTableSize: 8192, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 0);

        Assert.Equal(SettingsParameter.HeaderTableSize, key);
        Assert.Equal(8192u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_custom_max_frame_size_when_specified()
    {
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, DefaultWindow, 4096, maxFrameSize: 32768, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 3);

        Assert.Equal(SettingsParameter.MaxFrameSize, key);
        Assert.Equal(32768u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_enable_push_0()
    {
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, DefaultWindow, 4096, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 1);

        Assert.Equal(SettingsParameter.EnablePush, key);
        Assert.Equal(0u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void PrefaceBuilder_should_advertise_stream_window_as_initial_window_size_not_connection_window()
    {
        // RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE is the PER-STREAM window. Advertising the
        // (much larger) connection window here causes the peer to send more than one stream window
        // of DATA before the first WINDOW_UPDATE, driving the local stream window negative → false
        // FLOW_CONTROL_ERROR. The two windows must be distinct.
        const int streamWindow = DefaultWindow;
        const int connectionWindow = 64 * 1024 * 1024;

        var (owner, length) = PrefaceBuilder.Build(streamWindow, connectionWindow, 4096, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 2);

        Assert.Equal(SettingsParameter.InitialWindowSize, key);
        Assert.Equal((uint)streamWindow, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void PrefaceBuilder_should_size_window_update_from_connection_window_not_stream_window()
    {
        const int streamWindow = DefaultWindow;
        const int connectionWindow = 64 * 1024 * 1024;

        var (owner, length) = PrefaceBuilder.Build(streamWindow, connectionWindow, 4096, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];

        ParseSettings(span, out var hasWindowUpdate);
        Assert.True(hasWindowUpdate, "Expected WINDOW_UPDATE for connection window > 65535");

        var increment = BinaryPrimitives.ReadUInt32BigEndian(span[(length - 4)..]);
        Assert.Equal((uint)(connectionWindow - DefaultWindow), increment);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void PrefaceBuilder_should_include_window_update_when_connection_window_exceeds_65535()
    {
        const int largeConnectionWindow = 64 * 1024 * 1024;
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, largeConnectionWindow, 4096, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];

        ParseSettings(span, out var hasWindowUpdate);

        Assert.True(hasWindowUpdate, "Expected WINDOW_UPDATE frame for connection window > 65535");
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void PrefaceBuilder_should_not_include_window_update_when_connection_window_is_65535()
    {
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, DefaultWindow, 4096, 16 * 1024, DefaultHeaderListSize);
        var span = owner.Memory.Span[..length];

        ParseSettings(span, out var hasWindowUpdate);

        Assert.False(hasWindowUpdate, "No WINDOW_UPDATE expected when connection window == 65535");
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void PrefaceBuilder_should_advertise_max_header_list_size()
    {
        // Regression: the client enforced MaxResponseHeaderListSize locally but never advertised
        // SETTINGS_MAX_HEADER_LIST_SIZE, so the peer could not pre-trim oversized header blocks.
        var (owner, length) = PrefaceBuilder.Build(DefaultWindow, DefaultWindow, 4096, 16 * 1024,
            maxHeaderListSize: 256 * 1024);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 4);

        Assert.Equal(SettingsParameter.MaxHeaderListSize, key);
        Assert.Equal(256u * 1024, value);
        owner.Dispose();
    }
}