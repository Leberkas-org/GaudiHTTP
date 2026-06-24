namespace GaudiHTTP.Protocol.Multiplexed;

internal interface IStreamStatePool<TState> where TState : class
{
    TState Rent();
    void Return(TState state);
}
