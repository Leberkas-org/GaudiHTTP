namespace TurboHTTP.Server.Routing;

internal sealed class TurboRouteBuilder : ITurboRouteBuilder
{
    public ITurboRouteBuilder WithName(string name) => this;
    public ITurboRouteBuilder WithTags(params string[] tags) => this;
    public ITurboRouteBuilder WithDescription(string description) => this;
    public ITurboRouteBuilder WithRequestTimeout(TimeSpan timeout) => this;
}
