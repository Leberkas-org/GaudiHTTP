using GaudiHTTP.Streams.Stages.Server;

namespace GaudiHTTP.Protocol;

internal interface IProtocolSwitchCapable
{
    void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory);
}