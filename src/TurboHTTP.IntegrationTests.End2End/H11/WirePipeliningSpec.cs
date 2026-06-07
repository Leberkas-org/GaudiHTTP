using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H11;

/// <summary>
/// True HTTP/1.1 wire-level pipelining: multiple requests are written to one TCP connection
/// BEFORE any response is read, and the server must answer them in request order on that same
/// connection. Uses a raw socket (not the TurboHTTP client) to control wire framing directly.
/// </summary>
[Collection("H11")]
public sealed class WirePipeliningSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/p/{id:int}", (int id) => Results.Text($"RESP-{id}"));
    }

    [Fact(Timeout = 30000)]
    public async Task Http11_should_answer_pipelined_requests_in_order_on_one_connection()
    {
        var uri = new Uri(BaseUri);
        var host = uri.Authority;

        using var tcp = new TcpClient();
        tcp.NoDelay = true;
        await tcp.ConnectAsync(uri.Host, uri.Port, CancellationToken);
        await using var stream = tcp.GetStream();

        // Write THREE keep-alive requests back-to-back before reading anything.
        var pipelined =
            $"GET /p/1 HTTP/1.1\r\nHost: {host}\r\n\r\n" +
            $"GET /p/2 HTTP/1.1\r\nHost: {host}\r\n\r\n" +
            $"GET /p/3 HTTP/1.1\r\nHost: {host}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(pipelined), CancellationToken);

        // Read until all three responses arrive (or a short idle window elapses).
        var raw = await ReadUntilThreeResponsesAsync(stream);

        // All three responses came back on this single connection...
        Assert.True(3 == CountOccurrences(raw, "HTTP/1.1 200"),
            $"Expected 3 responses. Raw bytes ({raw.Length}):\n{raw}");

        // ...and in the order the requests were sent.
        var i1 = raw.IndexOf("RESP-1", StringComparison.Ordinal);
        var i2 = raw.IndexOf("RESP-2", StringComparison.Ordinal);
        var i3 = raw.IndexOf("RESP-3", StringComparison.Ordinal);
        Assert.True(i1 >= 0 && i2 > i1 && i3 > i2,
            $"Pipelined responses out of order or missing (i1={i1}, i2={i2}, i3={i3})");
    }

    private async Task<string> ReadUntilThreeResponsesAsync(NetworkStream stream)
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        idle.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            while (CountOccurrences(sb.ToString(), "HTTP/1.1 200") < 3)
            {
                var read = await stream.ReadAsync(buffer, idle.Token);
                if (read == 0)
                {
                    break;
                }

                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }
        catch (OperationCanceledException)
        {
            // Idle window elapsed — return whatever arrived so the assertion can report it.
        }

        return sb.ToString();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
