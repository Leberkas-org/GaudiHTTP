using GaudiHTTP.Protocol;
using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.Shared;

internal static class InMemoryTransportExtensions
{
    internal static InMemoryTransport ConnectTransport(
        this IServerStateMachine sm,
        byte[]? initialData = null,
        FakeServerOps? ops = null)
    {
        var transport = new InMemoryTransport();
        if (initialData is not null)
        {
            transport.Feed(initialData);
        }

        sm.DecodeClientData(new TransportConnected(ConnectionInfo.None, transport));

        if (ops is not null)
        {
            WaitAndDrain(sm, ops.BodyMessages);
        }

        return transport;
    }

    internal static InMemoryTransport ConnectTransport(
        this IClientStateMachine sm,
        byte[]? initialData = null,
        FakeClientOps? ops = null)
    {
        var transport = new InMemoryTransport();
        if (initialData is not null)
        {
            transport.Feed(initialData);
        }

        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        if (ops is not null)
        {
            WaitAndDrain(sm, ops.BodyMessages);
        }

        return transport;
    }

    internal static void FeedMore(
        this InMemoryTransport transport,
        IServerStateMachine sm,
        FakeServerOps ops,
        byte[] data)
    {
        transport.Feed(data);
        WaitAndDrain(sm, ops.BodyMessages);
    }

    internal static void FeedMore(
        this InMemoryTransport transport,
        IClientStateMachine sm,
        FakeClientOps ops,
        byte[] data)
    {
        transport.Feed(data);
        WaitAndDrain(sm, ops.BodyMessages);
    }

    private static void WaitAndDrain(IServerStateMachine sm, List<object> bodyMessages)
    {
        SpinWait.SpinUntil(() => bodyMessages.Count > 0, TimeSpan.FromSeconds(2));
        DrainAll(bodyMessages, msg => sm.OnBodyMessage(msg));
    }

    private static void WaitAndDrain(IClientStateMachine sm, List<object> bodyMessages)
    {
        SpinWait.SpinUntil(() => bodyMessages.Count > 0, TimeSpan.FromSeconds(2));
        DrainAll(bodyMessages, msg => sm.OnBodyMessage(msg));
    }

    private static void DrainAll(List<object> bodyMessages, Action<object> dispatch)
    {
        while (bodyMessages.Count > 0)
        {
            var snapshot = bodyMessages.ToArray();
            bodyMessages.Clear();
            foreach (var msg in snapshot)
            {
                dispatch(msg);
            }

            if (bodyMessages.Count == 0)
            {
                Thread.Sleep(1);
                SpinWait.SpinUntil(() => bodyMessages.Count > 0, TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
