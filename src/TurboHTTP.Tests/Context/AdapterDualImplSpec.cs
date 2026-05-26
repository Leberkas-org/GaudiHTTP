using Microsoft.AspNetCore.Http;
using TurboHTTP.Context;
using TurboHTTP.Context.Adapters;

namespace TurboHTTP.Tests.Context;

public sealed class AdapterDualImplSpec
{
    [Fact(Timeout = 5000)]
    public void TurboResponseHeaderDictionary_should_implement_ITurboHeaderDictionary()
    {
        var headers = new TurboResponseHeaderDictionary();
        Assert.IsAssignableFrom<ITurboHeaderDictionary>(headers);
    }

    [Fact(Timeout = 5000)]
    public void TurboResponseHeaderDictionary_should_implement_IHeaderDictionary()
    {
        var headers = new TurboResponseHeaderDictionary();
        Assert.IsAssignableFrom<IHeaderDictionary>(headers);
    }

    [Fact(Timeout = 5000)]
    public void ITurboHeaderDictionary_should_expose_same_values_as_IHeaderDictionary()
    {
        var headers = new TurboResponseHeaderDictionary();
        headers["Content-Type"] = "text/plain";

        var turbo = (ITurboHeaderDictionary)headers;
        var aspnet = (IHeaderDictionary)headers;

        Assert.Equal("text/plain", turbo["Content-Type"].ToString());
        Assert.Equal(aspnet["Content-Type"], turbo["Content-Type"]);
    }

    [Fact(Timeout = 5000)]
    public void TurboQueryCollection_should_implement_ITurboQueryCollection()
    {
        var query = new TurboQueryCollection("?key=value");
        Assert.IsAssignableFrom<ITurboQueryCollection>(query);
    }

    [Fact(Timeout = 5000)]
    public void ITurboQueryCollection_indexer_should_return_first_value()
    {
        var query = new TurboQueryCollection("?key=value");
        var turbo = (ITurboQueryCollection)query;

        Assert.Equal("value", turbo["key"]);
        Assert.Equal(1, turbo.Count);
        Assert.True(turbo.ContainsKey("key"));
    }

    [Fact(Timeout = 5000)]
    public void TurboRequestCookieCollection_should_implement_ITurboRequestCookieCollection()
    {
        var cookies = new TurboRequestCookieCollection("session=abc");
        Assert.IsAssignableFrom<ITurboRequestCookieCollection>(cookies);
    }

    [Fact(Timeout = 5000)]
    public void ITurboRequestCookieCollection_indexer_should_return_value()
    {
        var cookies = new TurboRequestCookieCollection("session=abc; theme=dark");
        var turbo = (ITurboRequestCookieCollection)cookies;

        Assert.Equal("abc", turbo["session"]);
        Assert.Equal("dark", turbo["theme"]);
        Assert.Equal(2, turbo.Count);
        Assert.True(turbo.ContainsKey("session"));
    }

    [Fact(Timeout = 5000)]
    public void TurboFormFile_should_implement_ITurboFormFile()
    {
        var file = new TurboFormFile("file", "test.txt", "text/plain", [1, 2, 3]);
        Assert.IsAssignableFrom<ITurboFormFile>(file);
    }

    [Fact(Timeout = 5000)]
    public void ITurboFormFile_should_expose_properties()
    {
        var file = new TurboFormFile("file", "test.txt", "text/plain", [1, 2, 3]);
        var turbo = (ITurboFormFile)file;

        Assert.Equal("file", turbo.Name);
        Assert.Equal("test.txt", turbo.FileName);
        Assert.Equal(3, turbo.Length);
        Assert.NotNull(turbo.OpenReadStream());
    }

    [Fact(Timeout = 5000)]
    public void TurboFormCollection_should_implement_ITurboFormCollection()
    {
        var fields = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["name"] = "test"
        };
        var files = new TurboFormFileCollection([]);
        var form = new TurboFormCollection(fields, files);
        Assert.IsAssignableFrom<ITurboFormCollection>(form);
    }
}
