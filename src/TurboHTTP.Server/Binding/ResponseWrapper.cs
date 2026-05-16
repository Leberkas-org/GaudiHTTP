using System.Net;
using System.Text.Json;

namespace TurboHTTP.Server.Binding;

internal static class ResponseWrapper
{
    internal static Func<object?, Task<HttpResponseMessage>> CreateWrapper(Type returnType)
    {
        if (returnType == typeof(HttpResponseMessage))
        {
            return value => Task.FromResult((HttpResponseMessage)value!);
        }

        if (returnType == typeof(string))
        {
            return value =>
            {
                if (value is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent((string)value, System.Text.Encoding.UTF8, "text/plain")
                };
                return Task.FromResult(response);
            };
        }

        if (returnType == typeof(void))
        {
            return CreateVoidWrapper();
        }

        return value =>
        {
            if (value is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (value is HttpResponseMessage msg)
            {
                return Task.FromResult(msg);
            }

            var json = JsonSerializer.Serialize(value);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        };
    }

    internal static Func<object?, Task<HttpResponseMessage>> CreateVoidWrapper()
    {
        return _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}
