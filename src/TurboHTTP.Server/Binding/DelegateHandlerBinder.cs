using System.Reflection;

namespace TurboHTTP.Server.Binding;

internal static class DelegateHandlerBinder
{
    internal static Func<TurboHttpContext, IServiceProvider, Task<HttpResponseMessage>> Bind(
        string pattern,
        Delegate handler)
    {
        var method = handler.Method;
        var parameters = method.GetParameters();
        var routeSegments = ExtractRouteSegments(pattern);

        var binders = new ParameterBinder[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            binders[i] = CreateBinder(parameters[i], routeSegments);
        }

        var returnType = method.ReturnType;
        var isAsync = returnType == typeof(Task)
                      || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                      || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

        var unwrappedReturnType = returnType;
        if (isAsync && returnType.IsGenericType)
        {
            unwrappedReturnType = returnType.GetGenericArguments()[0];
        }
        else if (returnType == typeof(Task) || returnType == typeof(void))
        {
            unwrappedReturnType = typeof(void);
        }

        var wrapper = unwrappedReturnType == typeof(void)
            ? ResponseWrapper.CreateVoidWrapper()
            : ResponseWrapper.CreateWrapper(unwrappedReturnType);

        return async (ctx, services) =>
        {
            var args = new object?[binders.Length];
            for (var i = 0; i < binders.Length; i++)
            {
                args[i] = binders[i].Bind(ctx, services);
            }

            var result = handler.DynamicInvoke(args);

            if (result is Task task)
            {
                await task;

                if (returnType.IsGenericType)
                {
                    var resultProperty = task.GetType().GetProperty("Result")!;
                    result = resultProperty.GetValue(task);
                }
                else
                {
                    return await wrapper(null);
                }
            }

            return await wrapper(result);
        };
    }

    private static ParameterBinder CreateBinder(ParameterInfo parameter, HashSet<string> routeSegments)
    {
        var type = parameter.ParameterType;
        var name = parameter.Name!;

        if (type == typeof(TurboHttpContext))
        {
            return new ContextBinder();
        }

        if (type == typeof(CancellationToken))
        {
            return new CancellationTokenBinder();
        }

        if (type == typeof(HttpRequestMessage))
        {
            return new RequestBinder();
        }

        if (routeSegments.Contains(name))
        {
            return new RouteValueBinder(name, type);
        }

        if (type.IsInterface || (type.IsClass && type != typeof(string)))
        {
            return new ServiceBinder(type);
        }

        return new QueryStringBinder(name, type);
    }

    private static HashSet<string> ExtractRouteSegments(string pattern)
    {
        var segments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = pattern.Split('/');
        foreach (var part in parts)
        {
            if (part.StartsWith('{') && part.EndsWith('}'))
            {
                segments.Add(part[1..^1]);
            }
        }
        return segments;
    }
}
