using System.Net;
using System.Text;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.AcceptanceTests.H11;

public sealed class ConcurrencySpec : ClientAcceptanceTestBase
{
    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\n");
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_5_parallel_gets()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/ping")
            {
                Version = HttpVersion.Version11
            };
            return await SendClientAsync(HttpVersion.Version11, request, (_, _) => BuildResponse("pong"));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_3_parallel_posts()
    {
        var tasks = Enumerable.Range(0, 3).Select(async i =>
        {
            var payload = $"body-{i}";
            var request = new HttpRequestMessage(HttpMethod.Post, "http://fake.test/echo")
            {
                Version = HttpVersion.Version11,
                Content = new StringContent(payload, Encoding.UTF8, "text/plain")
            };
            return await SendClientAsync(HttpVersion.Version11, request, (_, _) => BuildResponse(payload));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_sequential_burst_of_20_requests()
    {
        for (var i = 0; i < 20; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
            {
                Version = HttpVersion.Version11
            };

            var response = await SendClientAsync(HttpVersion.Version11, request, (_, _) => BuildResponse("Hello World"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_mixed_methods_concurrent()
    {
        var getTask1 = SendClientAsync(HttpVersion.Version11,
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello") { Version = HttpVersion.Version11 },
            (_, _) => BuildResponse("Hello World"));

        var getTask2 = SendClientAsync(HttpVersion.Version11,
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/ping") { Version = HttpVersion.Version11 },
            (_, _) => BuildResponse("pong"));

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://fake.test/echo")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("concurrent-post", Encoding.UTF8, "text/plain")
        };
        var postTask = SendClientAsync(HttpVersion.Version11, postRequest, (_, _) => BuildResponse("concurrent-post"));

        var putRequest = new HttpRequestMessage(HttpMethod.Put, "http://fake.test/echo")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("concurrent-put", Encoding.UTF8, "text/plain")
        };
        var putTask = SendClientAsync(HttpVersion.Version11, putRequest, (_, _) => BuildResponse("concurrent-put"));

        var getTask3 = SendClientAsync(HttpVersion.Version11,
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello") { Version = HttpVersion.Version11 },
            (_, _) => BuildResponse("Hello World"));

        var responses = await Task.WhenAll(getTask1, getTask2, postTask, putTask, getTask3);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
