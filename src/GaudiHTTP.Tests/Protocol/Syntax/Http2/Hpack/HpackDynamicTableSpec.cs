using System.Text;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Hpack;

public sealed class HpackDynamicTableSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_have_size_zero_when_empty()
    {
        var table = new HpackDynamicTable();
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_have_count_zero_when_empty()
    {
        var table = new HpackDynamicTable();
        Assert.Equal(0, table.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_have_max_size_4096_by_default()
    {
        var table = new HpackDynamicTable();
        Assert.Equal(4096, table.MaxSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_return_null_when_getting_entry_1_from_empty_table()
    {
        var table = new HpackDynamicTable();
        Assert.Null(table.GetEntry(1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_return_null_when_getting_entry_at_index_zero()
    {
        var table = new HpackDynamicTable();
        Assert.Null(table.GetEntry(0));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_have_correct_size_when_single_entry_is_added()
    {
        var table = new HpackDynamicTable();
        table.Add("via", "proxy1", 3, 6);
        Assert.Equal(41, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_accumulate_size_when_two_entries_are_added()
    {
        var table = new HpackDynamicTable();
        table.Add("via", "proxy1", 3, 6);
        table.Add("age", "100", 3, 3);
        Assert.Equal(79, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_add_32_bytes_when_adding_empty_name_and_value()
    {
        var table = new HpackDynamicTable();
        table.Add(string.Empty, string.Empty, 0, 0);
        Assert.Equal(32, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_count_size_as_utf8_bytes_when_name_contains_multibyte_characters()
    {
        var table = new HpackDynamicTable();
        const string name = "café";
        var nameBytes = Encoding.UTF8.GetByteCount(name);
        table.Add(name, string.Empty, nameBytes, 0);
        Assert.Equal(nameBytes + 0 + 32, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_count_size_as_utf8_bytes_when_value_contains_multibyte_characters()
    {
        var table = new HpackDynamicTable();
        const string value = "héllo";
        var valueBytes = Encoding.UTF8.GetByteCount(value);
        table.Add("x", value, 1, valueBytes);
        Assert.Equal(1 + valueBytes + 32, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_return_most_recent_entry_when_getting_entry_1()
    {
        var table = new HpackDynamicTable();
        table.Add("first", "v1", 5, 2);
        table.Add("second", "v2", 6, 2);

        var entry = table.GetEntry(1);
        Assert.NotNull(entry);
        Assert.Equal("second", entry.Value.Name);
        Assert.Equal("v2", entry.Value.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_return_second_most_recent_entry_when_getting_entry_2()
    {
        var table = new HpackDynamicTable();
        table.Add("first", "v1", 5, 2);
        table.Add("second", "v2", 6, 2);

        var entry = table.GetEntry(2);
        Assert.NotNull(entry);
        Assert.Equal("first", entry.Value.Name);
        Assert.Equal("v1", entry.Value.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_have_oldest_at_highest_index_when_entries_are_added_in_fifo_order()
    {
        var table = new HpackDynamicTable();
        table.Add("a", "1", 1, 1);
        table.Add("b", "2", 1, 1);
        table.Add("c", "3", 1, 1);

        Assert.Equal("c", table.GetEntry(1)!.Value.Name);
        Assert.Equal("b", table.GetEntry(2)!.Value.Name);
        Assert.Equal("a", table.GetEntry(3)!.Value.Name);
        Assert.Equal(3, table.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_return_null_when_getting_entry_beyond_count()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y", 1, 1);
        Assert.Null(table.GetEntry(2));
        Assert.Null(table.GetEntry(99));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_remove_oldest_entry_first_when_eviction_occurs()
    {
        var table = new HpackDynamicTable();
        table.Add("alpha", "1", 5, 1);
        table.Add("beta", "2", 4, 1);
        table.Add("gamma", "3", 5, 1);

        var gammaSize = "gamma".Length + "3".Length + 32;
        var betaSize = "beta".Length + "2".Length + 32;
        var newMax = gammaSize + betaSize;

        table.SetMaxSize(newMax);

        Assert.Equal(2, table.Count);
        Assert.Equal("gamma", table.GetEntry(1)!.Value.Name);
        Assert.Equal("beta", table.GetEntry(2)!.Value.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_clear_table_when_adding_oversized_entry()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y", 1, 1);
        Assert.Equal(1, table.Count);

        table.SetMaxSize(10);

        table.Add("longname", "longvalue", 8, 9);

        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_evict_all_entries_when_max_size_is_set_to_zero()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y", 1, 1);
        table.Add("a", "b", 1, 1);
        table.SetMaxSize(0);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_evict_oldest_to_fit_when_adding_to_full_table()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(68);

        table.Add("k", "1", 1, 1);
        table.Add("k", "2", 1, 1);

        Assert.Equal(2, table.Count);
        Assert.Equal(68, table.CurrentSize);

        table.Add("k", "3", 1, 1);
        Assert.Equal(2, table.Count);
        Assert.Equal(68, table.CurrentSize);
        Assert.Equal("3", table.GetEntry(1)!.Value.Value);
        Assert.Equal("2", table.GetEntry(2)!.Value.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_evict_multiple_old_entries_when_new_entry_requires_space()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(200);

        for (var i = 0; i < 5; i++)
        {
            var value = i.ToString();
            table.Add("h", value, 1, value.Length);
        }

        Assert.Equal(5, table.Count);

        table.Add("bigname", "bigvalue", 7, 8);

        Assert.Equal(47, table.GetEntry(1)!.Value.Name.Length + table.GetEntry(1)!.Value.Value.Length + 32);
        Assert.True(table.CurrentSize <= 200);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_update_max_size_when_set_max_size_is_called()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(1024);
        Assert.Equal(1024, table.MaxSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_not_change_entries_when_set_max_size_called_with_same_value()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y", 1, 1);
        var sizeBefore = table.CurrentSize;
        table.SetMaxSize(4096);
        Assert.Equal(sizeBefore, table.CurrentSize);
        Assert.Equal(1, table.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_throw_hpackexception_when_set_max_size_is_negative()
    {
        var table = new HpackDynamicTable();
        Assert.Throws<HpackException>(() => table.SetMaxSize(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_keep_entry_when_max_size_set_to_exact_entry_size()
    {
        var table = new HpackDynamicTable();
        table.Add("via", "proxy", 3, 5);
        table.SetMaxSize(40);
        Assert.Equal(1, table.Count);
        Assert.Equal(40, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_evict_entry_when_max_size_set_to_one_less_than_entry_size()
    {
        var table = new HpackDynamicTable();
        table.Add("via", "proxy", 3, 5);
        table.SetMaxSize(39);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_not_evict_when_table_fills_exactly_to_max_size()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(68);
        table.Add("k", "1", 1, 1);
        table.Add("k", "2", 1, 1);
        Assert.Equal(2, table.Count);
        Assert.Equal(68, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_evict_oldest_when_one_byte_beyond_max_size_is_added()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(67);
        table.Add("k", "1", 1, 1);
        table.Add("k", "2", 1, 1);
        Assert.Equal(1, table.Count);
        Assert.Equal("2", table.GetEntry(1)!.Value.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_keep_size_within_max_size_when_high_volume_entries_are_added()
    {
        var table = new HpackDynamicTable();
        table.SetMaxSize(200);

        for (var i = 0; i < 100; i++)
        {
            var value = i.ToString();
            table.Add("h", value, 1, value.Length);
        }

        Assert.True(table.CurrentSize <= 200, $"CurrentSize {table.CurrentSize} exceeds MaxSize 200");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_allow_new_entries_when_table_is_cleared_and_resized()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y", 1, 1);
        table.SetMaxSize(0);
        Assert.Equal(0, table.Count);

        table.SetMaxSize(4096);
        table.Add("new", "entry", 3, 5);
        Assert.Equal(1, table.Count);
        Assert.Equal("new", table.GetEntry(1)!.Value.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_return_null_when_getting_entry_at_negative_index()
    {
        var table = new HpackDynamicTable();
        table.Add("x", "y", 1, 1);
        Assert.Null(table.GetEntry(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_size_entry_by_provided_byte_lengths()
    {
        // The encoder/decoder compute the raw UTF-8 byte lengths while (de)serializing the header,
        // so Add reuses them for entry sizing instead of recomputing GetByteCount:
        // entrySize = name + value + 32.
        var table = new HpackDynamicTable();
        table.Add("via", "proxy1", nameByteLength: 3, valueByteLength: 6);
        Assert.Equal(3 + 6 + 32, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackDynamicTable_should_size_multibyte_entry_by_provided_utf8_byte_lengths()
    {
        const string name = "café";
        const string value = "héllo";
        var nameLen = Encoding.UTF8.GetByteCount(name);
        var valueLen = Encoding.UTF8.GetByteCount(value);

        var table = new HpackDynamicTable();
        table.Add(name, value, nameLen, valueLen);

        Assert.Equal(nameLen + valueLen + 32, table.CurrentSize);
    }
}
