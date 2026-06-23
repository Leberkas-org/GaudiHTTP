using Microsoft.Extensions.Primitives;
using TurboHTTP.Server.Context;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboResponseHeaderDictionaryReadOnlySpec
{
    [Fact(Timeout = 5000)]
    public void SetReadOnly_should_make_IsReadOnly_return_true()
    {
        var dict = new TurboHeaderDictionary();
        dict.Add("x-test", "value");

        dict.SetReadOnly();

        Assert.True(dict.IsReadOnly);
    }

    [Fact(Timeout = 5000)]
    public void SetReadOnly_should_throw_on_indexer_set()
    {
        var dict = new TurboHeaderDictionary();
        dict.SetReadOnly();

        Assert.Throws<InvalidOperationException>(() => dict["x-test"] = "value");
    }

    [Fact(Timeout = 5000)]
    public void SetReadOnly_should_throw_on_Add()
    {
        var dict = new TurboHeaderDictionary();
        dict.SetReadOnly();

        Assert.Throws<InvalidOperationException>(() => dict.Add("x-test", new StringValues("value")));
    }

    [Fact(Timeout = 5000)]
    public void SetReadOnly_should_throw_on_Remove()
    {
        var dict = new TurboHeaderDictionary();
        dict.Add("x-test", "value");
        dict.SetReadOnly();

        Assert.Throws<InvalidOperationException>(() => dict.Remove("x-test"));
    }

    [Fact(Timeout = 5000)]
    public void SetReadOnly_should_throw_on_Clear()
    {
        var dict = new TurboHeaderDictionary();
        dict.Add("x-test", "value");
        dict.SetReadOnly();

        Assert.Throws<InvalidOperationException>(() => dict.Clear());
    }

    [Fact(Timeout = 5000)]
    public void SetReadOnly_should_allow_reads()
    {
        var dict = new TurboHeaderDictionary();
        dict.Add("x-test", "value");
        dict.SetReadOnly();

        Assert.Equal("value", dict["x-test"]);
        Assert.True(dict.ContainsKey("x-test"));
        Assert.Equal(1, dict.Count);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_readonly_flag()
    {
        var dict = new TurboHeaderDictionary();
        dict.Add("x-test", "value");
        dict.SetReadOnly();

        dict.Reset();

        Assert.False(dict.IsReadOnly);
        dict.Add("x-new", "new-value");
        Assert.Equal("new-value", dict["x-new"]);
    }
}
