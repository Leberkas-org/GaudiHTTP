using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests MAX_CONCURRENT_STREAMS tracking in Http20Engine per RFC 9113 §6.5.2.
/// Verifies the engine exposes the configured limit and passes it to the underlying connection stage.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http20Engine"/>.
/// RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS indicates the maximum number of concurrent
/// streams that the sender will allow. Initially, there is no limit to this value.
/// </remarks>
public sealed class Http20EngineMaxConcurrentStreamsTests
{
    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-001: Default MaxConcurrentStreams is int.MaxValue (unlimited)")]
    public void Should_DefaultToIntMaxValue_When_NoMaxConcurrentStreamsSpecified()
    {
        var engine = new Http20Engine();
        Assert.Equal(int.MaxValue, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-002: Default parameterless constructor uses unlimited streams")]
    public void Should_UseUnlimitedStreams_When_ParameterlessConstructorUsed()
    {
        var engine = new Http20Engine();
        Assert.Equal(Http20Engine.DefaultMaxConcurrentStreams, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-003: Constructor with custom maxConcurrentStreams stores value")]
    public void Should_StoreCustomValue_When_MaxConcurrentStreamsProvidedInConstructor()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 50);
        Assert.Equal(50, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-004: MaxConcurrentStreams=1 stores single-stream limit")]
    public void Should_StoreSingleStreamLimit_When_MaxConcurrentStreamsIs1()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 1);
        Assert.Equal(1, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-005: MaxConcurrentStreams=0 stores zero value")]
    public void Should_StoreZeroValue_When_MaxConcurrentStreamsIs0()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 0);
        Assert.Equal(0, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-006: DefaultMaxConcurrentStreams constant equals int.MaxValue")]
    public void Should_HaveDefaultConstantEqualToIntMaxValue()
    {
        Assert.Equal(int.MaxValue, Http20Engine.DefaultMaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-007: Constructor with windowSize only uses default maxConcurrentStreams")]
    public void Should_UseDefaultMaxConcurrentStreams_When_OnlyWindowSizeSpecified()
    {
        var engine = new Http20Engine(initialWindowSize: 32768);
        Assert.Equal(int.MaxValue, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-008: CreateFlow does not throw with custom maxConcurrentStreams")]
    public void Should_CreateFlowSuccessfully_When_CustomMaxConcurrentStreamsSpecified()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 10);
        var flow = engine.CreateFlow();
        Assert.NotNull(flow);
    }

    [Theory(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-009: Various maxConcurrentStreams values stored correctly")]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Should_StoreCorrectValue_When_VariousMaxConcurrentStreamsProvided(int maxStreams)
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: maxStreams);
        Assert.Equal(maxStreams, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-010: SettingsFrame with MaxConcurrentStreams parameter decoded correctly")]
    public void Should_DecodeMaxConcurrentStreamsParameter_When_SettingsFrameContainsIt()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 42u)]);
        var parameter = frame.Parameters.Single();
        Assert.Equal(SettingsParameter.MaxConcurrentStreams, parameter.Item1);
        Assert.Equal(42u, parameter.Item2);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-011: SettingsFrame ACK has no parameters")]
    public void Should_HaveNoParameters_When_SettingsFrameIsAck()
    {
        var frame = new SettingsFrame([], isAck: true);
        Assert.True(frame.IsAck);
        Assert.Empty(frame.Parameters);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-6.5.2-ENG-MCS-012: SettingsFrame with multiple parameters includes MaxConcurrentStreams")]
    public void Should_IncludeMaxConcurrentStreams_When_SettingsFrameHasMultipleParameters()
    {
        var frame = new SettingsFrame([
            (SettingsParameter.InitialWindowSize, 32768u),
            (SettingsParameter.MaxConcurrentStreams, 128u),
            (SettingsParameter.MaxFrameSize, 16384u),
        ]);

        var maxStreams = frame.Parameters
            .Where(p => p.Item1 == SettingsParameter.MaxConcurrentStreams)
            .Select(p => (int)p.Item2)
            .SingleOrDefault();

        Assert.Equal(128, maxStreams);
    }
}
