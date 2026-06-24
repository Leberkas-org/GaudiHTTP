using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class WirePipeliningSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/p/{id:int}", (int id) => Results.Text($"RESP-{id}"));
    }

    [Fact(Timeout = 15000)]
    public async Task Http11_should_answer_pipelined_requests_in_order_on_one_connection()
    {
        var uri = new Uri(BaseUri);
        var host = uri.Authority;

        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(uri.Host, uri.Port, CancellationToken);
        await using var stream = tcp.GetStream();

        var pipelined =
            $"GET /p/1 HTTP/1.1\r\nHost: {host}\r\n\r\n" +
            $"GET /p/2 HTTP/1.1\r\nHost: {host}\r\n\r\n" +
            $"GET /p/3 HTTP/1.1\r\nHost: {host}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(pipelined), CancellationToken);

        var raw = await ReadUntilThreeResponsesAsync(stream, tcp.Client);

        Assert.True(3 == CountOccurrences(raw, "HTTP/1.1 200"),
            $"Expected 3 responses. Raw bytes ({raw.Length}):\n{raw}");

        var i1 = raw.IndexOf("RESP-1", StringComparison.Ordinal);
        var i2 = raw.IndexOf("RESP-2", StringComparison.Ordinal);
        var i3 = raw.IndexOf("RESP-3", StringComparison.Ordinal);
        Assert.True(i1 >= 0 && i2 > i1 && i3 > i2,
            $"Pipelined responses out of order or missing (i1={i1}, i2={i2}, i3={i3})");
    }

    private async Task<string> ReadUntilThreeResponsesAsync(NetworkStream stream, Socket socket)
    {
        socket.ReceiveTimeout = 10000;
        var sb = new StringBuilder();
        var buffer = new byte[4096];

        try
        {
            // Read until all three response bodies have arrived, not just their status lines: the
            // final response's header and body routinely land in separate TCP segments, so counting
            // "HTTP/1.1 200" alone would stop before the last body and race the wire. A genuinely
            // dropped or reordered body is still caught — its marker never arrives, the read blocks
            // to the receive timeout, and the in-order assertion below then fails.
            while (!HasAllThreeBodies(sb.ToString()))
            {
                var read = await Task.Run(() => stream.Read(buffer, 0, buffer.Length));
                if (read == 0)
                {
                    break;
                }

                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }
        catch (IOException) { _ = sb; }
        catch (SocketException) { _ = sb; }

        return sb.ToString();
    }

    private static bool HasAllThreeBodies(string raw)
        => raw.Contains("RESP-1", StringComparison.Ordinal)
           && raw.Contains("RESP-2", StringComparison.Ordinal)
           && raw.Contains("RESP-3", StringComparison.Ordinal);

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
