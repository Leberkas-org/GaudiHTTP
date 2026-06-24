using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Headers;

public sealed class HeaderCollectionSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_preserve_insertion_order()
    {
        var headers = new HeaderCollection
        {
            { "Host", "example.com" },
            { "User-Agent", "test/1.0" },
            { "Accept", "*/*" }
        };

        var names = headers.Select(h => h.Name).ToArray();
        Assert.Equal(new[] { "Host", "User-Agent", "Accept" }, names);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_allow_multiple_values_for_same_name()
    {
        var headers = new HeaderCollection
        {
            { "Set-Cookie", "a=1" },
            { "Set-Cookie", "b=2" }
        };

        Assert.Equal(new[] { "a=1", "b=2" }, headers.GetValues("Set-Cookie").ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_combine_values_with_comma_when_joining()
    {
        var headers = new HeaderCollection
        {
            { "Accept", "text/html" },
            { "Accept", "application/json" }
        };

        Assert.Equal("text/html, application/json", headers.GetCombined("Accept"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_lookup_case_insensitive()
    {
        var headers = new HeaderCollection { { "content-type", "text/html" } };

        Assert.Equal(new[] { "text/html" }, headers.GetValues("Content-Type").ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.3")]
    public void HeaderCollection_should_return_null_GetCombined_when_missing()
    {
        var headers = new HeaderCollection();
        Assert.Null(headers.GetCombined("Host"));
        Assert.Empty(headers.GetValues("Host"));
    }

    [Fact(Timeout = 5000)]
    public void HeaderCollection_should_clear_all_entries()
    {
        var headers = new HeaderCollection
        {
            { "A", "1" },
            { "B", "2" }
        };
        headers.Clear();
        Assert.Equal(0, headers.Count);
    }

    [Fact(Timeout = 5000)]
    public void GetCombined_should_return_the_single_value_instance_without_copying()
    {
        // A fresh, non-interned instance so Assert.Same proves no StringBuilder copy was made for
        // the single-value case (the common Content-Length / Transfer-Encoding path).
        var value = new string("text/html".ToCharArray());
        var headers = new HeaderCollection { { "Content-Type", value } };

        Assert.Same(value, headers.GetCombined("Content-Type"));
    }

    [Fact(Timeout = 5000)]
    public void Foreach_should_not_allocate_a_boxed_enumerator()
    {
        var headers = new HeaderCollection { { "A", "1" }, { "B", "2" } };
        foreach (var h in headers)
        {
            _ = h.Name;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        foreach (var h in headers)
        {
            count++;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(2, count);
        Assert.Equal(0, allocated);
    }
}