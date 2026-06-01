using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3SettingsSpec
{
    private static byte[] BuildSettingsFrame(params (long Id, long Value)[] parameters)
    {
        var frame = new SettingsFrame(parameters);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    private static MultiplexedData WrapAsControlStream(byte[] data)
    {
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return new MultiplexedData(buffer, CriticalStreamId.ControlId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void DecodeClientData_should_accept_first_SETTINGS_frame()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(new TurboServerOptions().ToHttp3Options(), ops);
        sm.PreStart();

        var settingsData = BuildSettingsFrame();
        sm.DecodeClientData(WrapAsControlStream(settingsData));

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void DecodeClientData_should_reject_second_SETTINGS_frame()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(new TurboServerOptions().ToHttp3Options(), ops);
        sm.PreStart();

        var settings1 = BuildSettingsFrame();
        sm.DecodeClientData(WrapAsControlStream(settings1));
        Assert.False(sm.ShouldComplete);

        var settings2 = BuildSettingsFrame();
        sm.DecodeClientData(WrapAsControlStream(settings2));

        Assert.True(sm.ShouldComplete);
    }
}
