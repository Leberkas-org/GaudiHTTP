using System.Net;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H11;

[Collection("H11")]
public sealed class ChannelApiSpec : IntegrationSpecBase
{
    public ChannelApiSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    private static readonly ProtocolVariant H11 = new(TestHttpVersion.H11, tls: false);

    [Fact(Timeout = 15000)]
    public async Task Channel_should_handle_get_roundtrip()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;

        await client.Requests.WriteAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.True(await client.Responses.WaitToReadAsync(CancellationToken));
        Assert.True(client.Responses.TryRead(out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        response.Dispose();
    }

    [Fact(Timeout = 15000)]
    public async Task Channel_should_handle_post_with_small_body()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;

        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new ByteArrayContent(new byte[1024])
        };
        await client.Requests.WriteAsync(request, CancellationToken);

        Assert.True(await client.Responses.WaitToReadAsync(CancellationToken));
        Assert.True(client.Responses.TryRead(out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        response.Dispose();
    }

    [Fact(Timeout = 30000)]
    public async Task Channel_should_handle_post_with_1mb_body()
    {
        await using var helper = CreateClient(H11);
        var client = helper.Client;

        var payload = new byte[1 * 1024 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new ByteArrayContent(payload)
        };
        await client.Requests.WriteAsync(request, CancellationToken);

        Assert.True(await client.Responses.WaitToReadAsync(CancellationToken));
        Assert.True(client.Responses.TryRead(out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("url", body);
        response.Dispose();
    }
}
