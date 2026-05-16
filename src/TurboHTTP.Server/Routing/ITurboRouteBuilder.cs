namespace TurboHTTP.Server.Routing;

public interface ITurboRouteBuilder
{
    ITurboRouteBuilder WithName(string name);
    ITurboRouteBuilder WithTags(params string[] tags);
    ITurboRouteBuilder WithDescription(string description);
    ITurboRouteBuilder WithRequestTimeout(TimeSpan timeout);
}
