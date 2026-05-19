namespace TurboHTTP.Server.Entity;

internal sealed class EntityMethodConfig
{
    public Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory { get; }
    public bool IsTell { get; }
    public TimeSpan? TimeoutOverride { get; }

    public EntityMethodConfig(
        Func<TurboHttpContext, IServiceProvider, ValueTask<object>> messageFactory,
        bool isTell,
        TimeSpan? timeoutOverride)
    {
        MessageFactory = messageFactory;
        IsTell = isTell;
        TimeoutOverride = timeoutOverride;
    }
}
