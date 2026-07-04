using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.FlowControl;

public sealed class FlowControllerReservationSpec
{
    private static FlowController CreateController(int connectionWindow = 64 * 1024, int streamWindow = 64 * 1024)
    {
        // Initial send windows are always 65535 per RFC 9113, so we initialize with
        // larger recv windows and let the constructor set send to 65535
        var fc = new FlowController(
            connectionWindowSize: connectionWindow,
            streamWindowSize: streamWindow);
        return fc;
    }

    [Fact(Timeout = 5000)]
    public void Reserve_should_decrement_both_windows()
    {
        var fc = CreateController(connectionWindow: 64 * 1024, streamWindow: 32 * 1024);
        fc.InitStreamSendWindow(1);

        fc.Reserve(1, 16 * 1024);

        Assert.Equal(65535 - 16 * 1024, fc.ConnectionSendWindow);
        Assert.Equal(65535 - 16 * 1024, fc.GetStreamSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void Refund_should_increment_both_windows()
    {
        var fc = CreateController(connectionWindow: 64 * 1024, streamWindow: 32 * 1024);
        fc.InitStreamSendWindow(1);
        fc.Reserve(1, 16 * 1024);

        fc.Refund(1, 4 * 1024);

        Assert.Equal(65535 - 12 * 1024, fc.ConnectionSendWindow);
        Assert.Equal(65535 - 12 * 1024, fc.GetStreamSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    public void Reserve_should_reflect_outstanding_reservations_in_connection_window()
    {
        var fc = CreateController(connectionWindow: 32 * 1024, streamWindow: 32 * 1024);
        fc.InitStreamSendWindow(1);
        fc.InitStreamSendWindow(3);

        fc.Reserve(1, 16 * 1024);
        fc.Reserve(3, 16 * 1024);

        Assert.Equal(65535 - 32 * 1024, fc.ConnectionSendWindow);
    }

    [Fact(Timeout = 5000)]
    public void Refund_with_zero_should_be_noop()
    {
        var fc = CreateController(connectionWindow: 64 * 1024, streamWindow: 32 * 1024);
        fc.InitStreamSendWindow(1);
        fc.Reserve(1, 16 * 1024);

        fc.Refund(1, 0);

        Assert.Equal(65535 - 16 * 1024, fc.ConnectionSendWindow);
    }
}
