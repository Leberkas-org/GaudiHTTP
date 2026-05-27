namespace TurboHTTP.Benchmarks.Internal;

public static class BenchmarkData
{
    public sealed record FortuneRow(int Id, string Message);

    private static readonly FortuneRow[] Rows = GenerateRows(10_000);

    public static readonly string FortunesHtml = GenerateFortunesHtml();

    public static FortuneRow GetRandomRow()
    {
        var index = Random.Shared.Next(0, Rows.Length);
        return Rows[index];
    }

    private static FortuneRow[] GenerateRows(int count)
    {
        var rows = new FortuneRow[count];
        for (var i = 0; i < count; i++)
        {
            rows[i] = new FortuneRow(i + 1, string.Concat("Fortune #", (i + 1).ToString()));
        }
        return rows;
    }

    private static string GenerateFortunesHtml()
    {
        var sb = new System.Text.StringBuilder(2 * 1024);
        sb.Append("<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>");
        for (var i = 0; i < 25; i++)
        {
            sb.Append("<tr><td>");
            sb.Append(i + 1);
            sb.Append("</td><td>");
            sb.Append(string.Concat("Fortune #", (i + 1).ToString()));
            sb.Append("</td></tr>");
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }
}
