using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3ClientConnectionErrorSpec
{
    private readonly FakeClientOps _clientOps = new();

    private Http3ClientStateMachine CreateMachine()
    {
        var sm = new Http3ClientStateMachine(new GaudiClientOptions(), _clientOps);
        sm.PreStart();
        sm.DecodeServerData(new TransportConnected(null!));
        _clientOps.Outbound.Clear();
        return sm;
    }

    private static TransportBuffer SerializeFrame(Http3Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void Repeated_stream_error_should_trigger_exactly_one_reconnect()
    {
        // QUIC reports a single connection failure as a StreamClosed(Error) PER stream (plus a trailing
        // TransportDisconnected). Each routes to OnConnectionLost; only the FIRST may start the reconnect.
        // Without idempotency the second call re-buffers an already-drained (empty) correlation map —
        // wiping the replay set — and emits a duplicate ConnectTransport.
        var sm = CreateMachine();
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/") { Version = new Version(3, 0) });
        _clientOps.Outbound.Clear();

        sm.DecodeServerData(new StreamClosed(0, DisconnectReason.Error));
        sm.DecodeServerData(new StreamClosed(4, DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Single(_clientOps.Outbound, o => o is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void Connection_failure_as_stream_errors_then_disconnect_should_reconnect_once()
    {
        // The full QUIC connection-failure signal pattern that an AutoReconnect=false (TCP-style) transport
        // emits: StreamClosed(Error) per stream, then a trailing TransportDisconnected. The state machine
        // must coalesce all of it into exactly ONE reconnect — the trailing disconnect is the same failure,
        // not a failed reconnect attempt (which would emit a second ConnectTransport / burn an attempt).
        var sm = CreateMachine();
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/") { Version = new Version(3, 0) });
        _clientOps.Outbound.Clear();

        sm.DecodeServerData(new StreamClosed(0, DisconnectReason.Error));
        sm.DecodeServerData(new StreamClosed(4, DisconnectReason.Error));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Single(_clientOps.Outbound, o => o is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Second_settings_frame_on_control_stream_should_disconnect()
    {
        // RFC 9114 §7.2.4: a second SETTINGS frame on the control stream is a connection error.
        // It must tear the connection down, not be silently absorbed.
        var sm = CreateMachine();

        sm.DecodeServerData(MultiplexedData.Rent(
            SerializeFrame(new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 16384)])), -2));
        sm.DecodeServerData(MultiplexedData.Rent(
            SerializeFrame(new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 16384)])), -2));

        Assert.Contains(_clientOps.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void Goaway_with_invalid_stream_id_should_disconnect()
    {
        // RFC 9114 §5.2: a GOAWAY stream ID that is not a client-initiated bidirectional ID
        // (not divisible by 4) is a connection error. It must not be swallowed.
        var sm = CreateMachine();

        sm.DecodeServerData(MultiplexedData.Rent(SerializeFrame(new GoAwayFrame(3)), -2));

        Assert.Contains(_clientOps.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.2")]
    public void Malformed_response_header_block_should_disconnect()
    {
        // RFC 9204 §2.2: a HEADERS frame whose QPACK field section indexes a static-table entry far out
        // of range desynchronizes the decoder — a connection error. The decode loop must not let it
        // escape uncaught; it tears the connection down.
        var sm = CreateMachine();

        // 2-byte field-section prefix (RIC=0, Base=0) + indexed-static line 0xFF + varint(137) -> index 200.
        var headerBlock = new byte[] { 0x00, 0x00, 0xFF, 0x89, 0x01 };
        var frame = new HeadersFrame(headerBlock);

        sm.DecodeServerData(MultiplexedData.Rent(SerializeFrame(frame), 0));

        Assert.Contains(_clientOps.Outbound, o => o is DisconnectTransport);
    }
}
