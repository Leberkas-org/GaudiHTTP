using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3QpackStreamErrorSpec
{
    private readonly FakeClientOps _clientOps = new();

    private Http3ClientStateMachine CreateMachine()
    {
        var sm = new Http3ClientStateMachine(new TurboClientOptions(), _clientOps);
        sm.PreStart();
        sm.DecodeServerData(new TransportConnected(null!));
        _clientOps.Outbound.Clear();
        return sm;
    }

    private static TransportBuffer Wrap(byte[] bytes)
    {
        var buf = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return buf;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.2")]
    public void Invalid_qpack_encoder_instruction_should_disconnect_the_connection()
    {
        // RFC 9204 §2.2 / §3.2.4: an encoder instruction that references a non-existent dynamic-table
        // entry is a QPACK_ENCODER_STREAM_ERROR — a connection error. It must NOT be silently absorbed.
        // Bytes: 0x80 = Insert With Name Reference, dynamic (T=0), name index 0; 0x00 = empty literal value.
        // The dynamic table is empty, so index 0 cannot be resolved.
        var sm = CreateMachine();

        sm.DecodeServerData(MultiplexedData.Rent(Wrap([0x80, 0x00]), -3));

        Assert.Contains(_clientOps.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.2")]
    public void Valid_qpack_encoder_instruction_should_not_disconnect()
    {
        // Insert With Literal Name (01Hxxxxx): 0x40 = literal name, H=0, len=0 (empty name);
        // 0x00 = empty literal value. A well-formed insert must not tear the connection down.
        var sm = CreateMachine();

        sm.DecodeServerData(MultiplexedData.Rent(Wrap([0x40, 0x00]), -3));

        Assert.DoesNotContain(_clientOps.Outbound, o => o is DisconnectTransport);
    }
}
