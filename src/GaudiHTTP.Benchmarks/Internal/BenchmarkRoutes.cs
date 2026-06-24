using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Benchmarks.Internal;

public static class BenchmarkRoutes
{
    /// <summary>Reusable 64 KB chunk for the streaming /download endpoint (server-side, not measured).</summary>
    private static readonly byte[] DownloadChunk = new byte[64 * 1024];

    public static void Register(WebApplication app, IAllocationProfiler? profiler = null)
    {
        // Server-process GC counters for out-of-process allocation measurement.
        // Format: "{allocatedBytes};{gen0};{gen1};{gen2}". Hit only twice per run (negligible).
        app.MapGet("/__allocstats", () =>
            Results.Text(string.Join(
                ';',
                GC.GetTotalAllocatedBytes(precise: true),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2))));

        // Server-only per-type allocation capture (serve child only; no-op when profiler is null).
        // /__allocreset clears the accumulated counts so the capture window excludes warmup;
        // /__alloctypes returns the top types BY HITS as "{hits}\t{sampledBytes}\t{typeName}" lines.
        app.MapGet("/__allocreset", () =>
        {
            profiler?.Reset();
            return Results.Text("ok");
        });

        app.MapGet("/__alloctypes", () =>
            Results.Text(profiler?.ReportText() ?? ""));

        app.MapGet("/benchmark/simple", () =>
            Results.Content("OK\n", "text/plain"));

        app.MapPost("/benchmark/payload", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            var response = System.Text.Encoding.UTF8.GetBytes(
                string.Concat("received:", received.Length.ToString()));
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });

        app.MapGet("/plaintext", () =>
            Results.Content("Hello, World!", "text/plain"));

        app.MapGet("/json", () =>
            Results.Json(new { message = "Hello, World!" }));

        app.MapGet("/db", () =>
        {
            var row = BenchmarkData.GetRandomRow();
            return Results.Json(row);
        });

        app.MapGet("/queries", (int? queries) =>
        {
            var count = Math.Clamp(queries ?? 1, 1, 500);
            var rows = new BenchmarkData.FortuneRow[count];
            for (var i = 0; i < count; i++)
            {
                rows[i] = BenchmarkData.GetRandomRow();
            }
            return Results.Json(rows);
        });

        app.MapGet("/fortunes", () =>
            Results.Content(BenchmarkData.FortunesHtml, "text/html; charset=utf-8"));

        app.MapPost("/echo", async ctx =>
        {
            ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentLength = ctx.Request.ContentLength;
            await ctx.Request.Body.CopyToAsync(ctx.Response.Body);
        });

        app.MapPost("/upload", async ctx =>
        {
            long count = 0;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
            {
                count += read;
            }
            var response = string.Concat("received:", count.ToString());
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(response);
        });

        app.MapGet("/download", async ctx =>
        {
            var size = 1 * 1024 * 1024;
            if (ctx.Request.Query.TryGetValue("size", out var raw)
                && int.TryParse(raw.ToString(), out var parsed) && parsed > 0)
            {
                size = parsed;
            }

            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = size;

            var remaining = size;
            while (remaining > 0)
            {
                var n = Math.Min(remaining, DownloadChunk.Length);
                await ctx.Response.Body.WriteAsync(DownloadChunk.AsMemory(0, n));
                remaining -= n;
            }
        });
    }
}
