using System.Globalization;
using System.Text.Json;

namespace TurboHTTP.Server.Binding;

internal abstract class ParameterBinder
{
    public abstract object? Bind(TurboHttpContext ctx, IServiceProvider services);
}

internal sealed class ContextBinder : ParameterBinder
{
    public override object Bind(TurboHttpContext ctx, IServiceProvider services) => ctx;
}

internal sealed class CancellationTokenBinder : ParameterBinder
{
    public override object Bind(TurboHttpContext ctx, IServiceProvider services) => ctx.RequestAborted;
}

internal sealed class RequestBinder : ParameterBinder
{
    public override object Bind(TurboHttpContext ctx, IServiceProvider services) => ctx.Request;
}

internal sealed class RouteValueBinder : ParameterBinder
{
    private readonly string _name;
    private readonly Type _type;

    public RouteValueBinder(string name, Type type)
    {
        _name = name;
        _type = type;
    }

    public override object? Bind(TurboHttpContext ctx, IServiceProvider services)
    {
        if (!ctx.RouteValues.TryGetValue(_name, out var value) || value is null)
        {
            return _type.IsValueType ? Activator.CreateInstance(_type) : null;
        }

        var str = value.ToString()!;
        return ParseValue(str, _type);
    }

    internal static object ParseValue(string str, Type type)
    {
        if (type == typeof(string))
        {
            return str;
        }

        if (type == typeof(int))
        {
            return int.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(long))
        {
            return long.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(float))
        {
            return float.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(double))
        {
            return double.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(decimal))
        {
            return decimal.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(bool))
        {
            return bool.Parse(str);
        }

        if (type == typeof(Guid))
        {
            return Guid.Parse(str);
        }

        if (type == typeof(DateTime))
        {
            return DateTime.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(str, CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.Parse(str, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(str, type, CultureInfo.InvariantCulture);
    }
}

internal sealed class QueryStringBinder : ParameterBinder
{
    private readonly string _name;
    private readonly Type _type;

    public QueryStringBinder(string name, Type type)
    {
        _name = name;
        _type = type;
    }

    public override object? Bind(TurboHttpContext ctx, IServiceProvider services)
    {
        var uri = ctx.Request.RequestUri;
        if (uri?.Query is not { Length: > 0 } query)
        {
            return _type.IsValueType ? Activator.CreateInstance(_type) : null;
        }

        var pairs = query.TrimStart('?').Split('&');
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], _name, StringComparison.OrdinalIgnoreCase))
            {
                return RouteValueBinder.ParseValue(Uri.UnescapeDataString(kv[1]), _type);
            }
        }

        return _type.IsValueType ? Activator.CreateInstance(_type) : null;
    }
}

internal sealed class ServiceBinder : ParameterBinder
{
    private readonly Type _serviceType;

    public ServiceBinder(Type serviceType)
    {
        _serviceType = serviceType;
    }

    public override object? Bind(TurboHttpContext ctx, IServiceProvider services)
    {
        return services.GetService(_serviceType);
    }
}

internal sealed class JsonBodyBinder : ParameterBinder
{
    private readonly Type _type;

    public JsonBodyBinder(Type type)
    {
        _type = type;
    }

    public override object? Bind(TurboHttpContext ctx, IServiceProvider services)
    {
        if (ctx.Request.Content is null)
        {
            return null;
        }

        var stream = ctx.Request.Content.ReadAsStream();
        return JsonSerializer.Deserialize(stream, _type);
    }
}
