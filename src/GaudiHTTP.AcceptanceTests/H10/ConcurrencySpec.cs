using System.Net;
using System.Text;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class ConcurrencySpec : ClientAcceptanceTestBase
{
    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\n");
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Concurrency_should_succeed_with_three_parallel_gets()
    {
        var tasks = Enumerable.Range(0, 3).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/ping")
            {
                Version = HttpVersion.Version10
            };
            return await SendClientAsync(HttpVersion.Version10, request, (_, _) => BuildResponse("pong"));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Concurrency_should_succeed_with_sequential_burst_of_10_requests()
    {
        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
            {
                Version = HttpVersion.Version10
            };

            var response = await SendClientAsync(HttpVersion.Version10, request, (_, _) => BuildResponse("Hello World"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Concurrency_should_succeed_with_mixed_get_and_post_concurrent_requests()
    {
        var getTask1 = SendClientAsync(HttpVersion.Version10,
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello") { Version = HttpVersion.Version10 },
            (_, _) => BuildResponse("Hello World"));

        var getTask2 = SendClientAsync(HttpVersion.Version10,
            new HttpRequestMessage(HttpMethod.Get, "http://fake.test/ping") { Version = HttpVersion.Version10 },
            (_, _) => BuildResponse("pong"));

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://fake.test/echo")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent("h10-post", Encoding.UTF8, "text/plain")
        };
        var postTask = SendClientAsync(HttpVersion.Version10, postRequest, (_, _) => BuildResponse("h10-post"));

        var responses = await Task.WhenAll(getTask1, getTask2, postTask);

        Assert.Equal(3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
