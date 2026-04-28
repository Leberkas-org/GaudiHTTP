using System.Net;
using Servus.Akka.IO;
using Servus.Akka.IO.Quic;
using Servus.Akka.Tests.Utils;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicTransportEventSpec
{
    [Fact(Timeout = 5000)]
    public void RequestLeaseAcquired_should_preserve_fields()
    {
        var lease = CreateTestStreamLease();
        var evt = new RequestLeaseAcquired(lease, 42);

        Assert.Same(lease, evt.Lease);
        Assert.Equal(42, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void TypedLeaseAcquired_should_preserve_fields()
    {
        var lease = CreateTestStreamLease();
        var evt = new TypedLeaseAcquired(lease, 0x00, 7);

        Assert.Same(lease, evt.Lease);
        Assert.Equal(0x00, evt.StreamTypeValue);
        Assert.Equal(7, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void AcquisitionFailed_should_preserve_error()
    {
        var ex = new IOException("test");
        var evt = new AcquisitionFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void InboundData_should_preserve_fields()
    {
        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var evt = new InboundData(buf, 5);

        Assert.Same(buf, evt.Item);
        Assert.Equal(5, evt.Gen);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void InboundComplete_should_preserve_fields()
    {
        var evt = new InboundComplete(QuicCloseKind.ConnectionFailure, 3, 42);

        Assert.Equal(QuicCloseKind.ConnectionFailure, evt.CloseKind);
        Assert.Equal(3, evt.Gen);
        Assert.Equal(42, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void InboundPumpFailed_should_preserve_fields()
    {
        var ex = new IOException("pump failed");
        var evt = new InboundPumpFailed(ex, 99);

        Assert.Same(ex, evt.Error);
        Assert.Equal(99, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteDone_should_implement_interface()
    {
        IQuicTransportEvent evt = new OutboundWriteDone();

        Assert.IsType<OutboundWriteDone>(evt);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteFailed_should_preserve_error()
    {
        var ex = new IOException("write failed");
        var evt = new OutboundWriteFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void EarlyDataRejected_should_preserve_buffer()
    {
        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var evt = new EarlyDataRejected(buf);

        Assert.Same(buf, evt.Buffer);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ConnectionMigrated_should_preserve_endpoints()
    {
        var oldEp = new IPEndPoint(IPAddress.Loopback, 1234);
        var newEp = new IPEndPoint(IPAddress.Loopback, 5678);
        var evt = new ConnectionMigrated(oldEp, newEp);

        Assert.Equal(oldEp, evt.OldLocalEndPoint);
        Assert.Equal(newEp, evt.NewLocalEndPoint);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionMigrated_should_allow_null_endpoints()
    {
        var evt = new ConnectionMigrated(null, null);

        Assert.Null(evt.OldLocalEndPoint);
        Assert.Null(evt.NewLocalEndPoint);
    }

    [Fact(Timeout = 5000)]
    public void InboundComplete_equality_should_compare_all_fields()
    {
        var a = new InboundComplete(QuicCloseKind.RequestStreamComplete, 1, 42);
        var b = new InboundComplete(QuicCloseKind.RequestStreamComplete, 1, 42);
        var c = new InboundComplete(QuicCloseKind.ConnectionFailure, 1, 42);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    private static QuicStreamLease CreateTestStreamLease()
    {
        var key = new RequestEndpoint
        {
            Scheme = "https",
            Host = "localhost",
            Port = 443,
            Version = new Version(3, 0)
        };
        var handle = new StreamHandle(Stream.Null, key, onWritesComplete: null);
        return new QuicStreamLease(handle);
    }
}

#pragma warning restore CA1416
