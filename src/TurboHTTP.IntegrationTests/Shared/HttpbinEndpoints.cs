using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.IntegrationTests.Shared;

internal static class HttpbinEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/get", HandleGet);
        app.MapPost("/post", HandlePost);
        app.MapPut("/put", HandlePut);
        app.MapPatch("/patch", HandlePatch);
        app.MapDelete("/delete", HandleDelete);
        app.MapGet("/headers", HandleHeaders);
        app.MapGet("/status/{code}", HandleStatus);
        app.MapGet("/bytes/{n}", HandleBytes);
        app.MapGet("/cookies", HandleGetCookies);
        app.MapGet("/cookies/set", HandleSetCookies);
        app.MapGet("/redirect/{n}", HandleRedirect);
        app.MapGet("/redirect-to", HandleRedirectTo);
        app.MapGet("/basic-auth/{user}/{pass}", HandleBasicAuth);
        app.MapGet("/cache", HandleCache);
        app.MapGet("/cache/{seconds}", HandleCacheWithSeconds);
        app.MapGet("/etag/{value}", HandleEtag);
        app.MapGet("/response-headers", HandleResponseHeaders);
        app.MapGet("/stream/{n:int}", HandleStream);
        app.MapGet("/gzip", HandleGzip);
        app.MapGet("/deflate", HandleDeflate);
    }

    private static async Task HandleGet(HttpContext ctx)
    {
        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandlePost(HttpContext ctx)
    {
        var response = await BuildMethodBodyResponse(ctx, "POST");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandlePut(HttpContext ctx)
    {
        var response = await BuildMethodBodyResponse(ctx, "PUT");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandlePatch(HttpContext ctx)
    {
        var response = await BuildMethodBodyResponse(ctx, "PATCH");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleDelete(HttpContext ctx)
    {
        var response = BuildEchoResponse(ctx, "DELETE");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleHeaders(HttpContext ctx)
    {
        var headersDict = BuildHeadersObject(ctx);
        var response = new { headers = headersDict };
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleStatus(HttpContext ctx, int code)
    {
        ctx.Response.StatusCode = code;
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleBytes(HttpContext ctx, int n)
    {
        ctx.Response.ContentType = "application/octet-stream";
        var buffer = new byte[n];
        RandomNumberGenerator.Fill(buffer);
        await ctx.Response.Body.WriteAsync(buffer);
    }

    private static async Task HandleGetCookies(HttpContext ctx)
    {
        var cookies = new JsonObject();
        foreach (var cookie in ctx.Request.Cookies)
        {
            cookies[cookie.Key] = cookie.Value;
        }
        await ctx.Response.WriteAsJsonAsync(cookies);
    }

    private static async Task HandleSetCookies(HttpContext ctx)
    {
        var query = ctx.Request.Query;
        foreach (var kvp in query)
        {
            ctx.Response.Cookies.Append(kvp.Key, kvp.Value.ToString(), new CookieOptions { Path = "/" });
        }
        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect("/cookies", permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleRedirect(HttpContext ctx, int n)
    {
        var redirectUrl = n <= 1 ? "/get" : string.Concat("/redirect/", n - 1);
        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect(redirectUrl, permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleRedirectTo(HttpContext ctx)
    {
        var url = ctx.Request.Query["url"].ToString();
        ctx.Response.StatusCode = 302;
        ctx.Response.Redirect(url, permanent: false);
        await ctx.Response.CompleteAsync();
    }

    private static async Task HandleBasicAuth(HttpContext ctx, string user, string pass)
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        var isValid = ValidateBasicAuth(authHeader, user, pass);

        if (!isValid)
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Fake Realm\"";
            await ctx.Response.CompleteAsync();
            return;
        }

        var response = new { authenticated = true, user };
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleCache(HttpContext ctx)
    {
        var etag = "\"cache-etag\"";
        var lastModified = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");

        var ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString();
        var ifModifiedSince = ctx.Request.Headers.IfModifiedSince.ToString();

        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(etag))
        {
            ctx.Response.StatusCode = 304;
            await ctx.Response.CompleteAsync();
            return;
        }

        if (!string.IsNullOrEmpty(ifModifiedSince))
        {
            ctx.Response.StatusCode = 304;
            await ctx.Response.CompleteAsync();
            return;
        }

        ctx.Response.Headers.ETag = etag;
        ctx.Response.Headers.LastModified = lastModified;

        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleCacheWithSeconds(HttpContext ctx, int seconds)
    {
        ctx.Response.Headers.CacheControl = string.Concat("public, max-age=", seconds);
        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleEtag(HttpContext ctx, string value)
    {
        var etag = string.Concat("\"", value, "\"");
        var ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString();

        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(value))
        {
            ctx.Response.StatusCode = 304;
            await ctx.Response.CompleteAsync();
            return;
        }

        ctx.Response.Headers.ETag = etag;
        var response = BuildEchoResponse(ctx, "GET");
        await ctx.Response.WriteAsJsonAsync(response);
    }

    private static async Task HandleStream(HttpContext ctx, int n)
    {
        ctx.Response.ContentType = "application/json";
        for (var i = 0; i < n; i++)
        {
            var line = JsonSerializer.Serialize(new
            {
                id = i,
                origin = GetClientOrigin(ctx),
                url = GetFullUrl(ctx)
            });
            await ctx.Response.WriteAsync(line + "\n");
            await ctx.Response.Body.FlushAsync();
        }
    }

    private static async Task HandleResponseHeaders(HttpContext ctx)
    {
        foreach (var (key, value) in ctx.Request.Query)
        {
            ctx.Response.Headers.Append(key, value.ToString());
        }

        await ctx.Response.WriteAsJsonAsync(BuildEchoResponse(ctx, "GET"));
    }

    private static async Task HandleGzip(HttpContext ctx)
    {
        var jsonBytes = BuildCompressionPayload(ctx, gzipped: true);
        var compressed = CompressBytes(jsonBytes, CompressionType.Gzip);

        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.ContentEncoding = "gzip";
        ctx.Response.ContentLength = compressed.Length;
        await ctx.Response.Body.WriteAsync(compressed);
    }

    private static async Task HandleDeflate(HttpContext ctx)
    {
        var jsonBytes = BuildCompressionPayload(ctx, gzipped: false);
        var compressed = CompressBytes(jsonBytes, CompressionType.Deflate);

        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.ContentEncoding = "deflate";
        ctx.Response.ContentLength = compressed.Length;
        await ctx.Response.Body.WriteAsync(compressed);
    }

    private static byte[] BuildCompressionPayload(HttpContext ctx, bool gzipped)
    {
        var headersDict = BuildHeadersObject(ctx);
        object payload = gzipped
            ? new { gzipped = true, headers = headersDict, origin = GetClientOrigin(ctx), method = "GET" }
            : new { deflated = true, headers = headersDict, origin = GetClientOrigin(ctx), method = "GET" };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static byte[] CompressBytes(byte[] data, CompressionType type)
    {
        using var ms = new MemoryStream();
        using (Stream stream = type switch
        {
            CompressionType.Gzip => new GZipStream(ms, CompressionLevel.Fastest),
            // HTTP "deflate" is actually zlib-wrapped, not raw deflate
            CompressionType.Deflate => new ZLibStream(ms, CompressionLevel.Fastest),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        })
        {
            stream.Write(data);
        }

        return ms.ToArray();
    }

    private enum CompressionType { Gzip, Deflate }

    private static JsonObject BuildEchoResponse(HttpContext ctx, string method)
    {
        var response = new JsonObject
        {
            ["args"] = BuildArgsObject(ctx),
            ["headers"] = BuildHeadersNode(ctx),
            ["origin"] = GetClientOrigin(ctx),
            ["url"] = GetFullUrl(ctx),
            ["method"] = method
        };
        return response;
    }

    private static async Task<JsonObject> BuildMethodBodyResponse(HttpContext ctx, string method)
    {
        var body = await ReadBodyAsString(ctx);
        var contentType = ctx.Request.ContentType ?? "";

        var response = new JsonObject
        {
            ["args"] = BuildArgsObject(ctx),
            ["data"] = body,
            ["headers"] = BuildHeadersNode(ctx),
            ["origin"] = GetClientOrigin(ctx),
            ["url"] = GetFullUrl(ctx),
            ["method"] = method
        };

        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);
                response["json"] = JsonNode.Parse(jsonElement.GetRawText());
            }
            catch
            {
                response["json"] = null;
            }
            response["form"] = new JsonObject();
        }
        else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var formDict = ParseFormData(body);
            var formObject = new JsonObject();
            foreach (var kvp in formDict)
            {
                formObject[kvp.Key] = kvp.Value;
            }
            response["form"] = formObject;
            response["json"] = null;
        }
        else
        {
            response["form"] = new JsonObject();
            response["json"] = null;
        }

        return response;
    }

    private static JsonObject BuildArgsObject(HttpContext ctx)
    {
        var args = new JsonObject();
        foreach (var kvp in ctx.Request.Query)
        {
            args[kvp.Key] = kvp.Value.ToString();
        }
        return args;
    }

    private static JsonNode BuildHeadersNode(HttpContext ctx)
    {
        var headersDict = BuildHeadersObject(ctx);
        var node = new JsonObject();
        foreach (var kvp in headersDict)
        {
            node[kvp.Key] = kvp.Value switch
            {
                string str => str,
                string[] arr => arr[0],
                _ => null
            };
        }
        return node;
    }

    private static Dictionary<string, object> BuildHeadersObject(HttpContext ctx)
    {
        var headers = new Dictionary<string, object>();
        foreach (var kvp in ctx.Request.Headers)
        {
            if (kvp.Value.Count == 1)
            {
                headers[kvp.Key] = kvp.Value[0] ?? "";
            }
            else
            {
                headers[kvp.Key] = kvp.Value.ToArray();
            }
        }
        return headers;
    }

    private static string GetFullUrl(HttpContext ctx)
    {
        var scheme = ctx.Request.Scheme;
        var host = ctx.Request.Host.ToString();
        var path = ctx.Request.Path.ToString();
        var query = ctx.Request.QueryString.ToString();
        return string.Concat(scheme, "://", host, path, query);
    }

    private static string GetClientOrigin(HttpContext ctx)
    {
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }

    private static async Task<string> ReadBodyAsString(HttpContext ctx)
    {
        ctx.Request.EnableBuffering();
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            var body = await reader.ReadToEndAsync();
            ctx.Request.Body.Position = 0;
            return body;
        }
    }

    private static Dictionary<string, string> ParseFormData(string body)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(body))
        {
            return result;
        }

        var pairs = body.Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2)
            {
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                result[key] = value;
            }
        }
        return result;
    }

    private static bool ValidateBasicAuth(string authHeader, string expectedUser, string expectedPass)
    {
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var base64 = authHeader["Basic ".Length..];
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var colonIndex = decoded.IndexOf(':');
            if (colonIndex < 0)
            {
                return false;
            }

            var user = decoded[..colonIndex];
            var pass = decoded[(colonIndex + 1)..];

            return user == expectedUser && pass == expectedPass;
        }
        catch
        {
            return false;
        }
    }
}
