using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Server;

public static class TurboResults
{
    public static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    public static HttpResponseMessage Ok<T>(T value) => JsonResponse(HttpStatusCode.OK, value);

    public static HttpResponseMessage Created<T>(T value) => JsonResponse(HttpStatusCode.Created, value);

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    public static HttpResponseMessage BadRequest() => new(HttpStatusCode.BadRequest);

    public static HttpResponseMessage BadRequest<T>(T error) => JsonResponse(HttpStatusCode.BadRequest, error);

    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    public static HttpResponseMessage Json<T>(T value, HttpStatusCode statusCode = HttpStatusCode.OK)
        => JsonResponse(statusCode, value);

    public static HttpResponseMessage EventStream(Source<string, NotUsed> source)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return response;
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return response;
    }
}
