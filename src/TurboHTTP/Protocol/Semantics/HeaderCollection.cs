using System.Collections;
using System.Text;

namespace TurboHTTP.Protocol.Semantics;

internal readonly struct HeaderEntry
{
    public string Name { get; }
    public string Value { get; }

    public HeaderEntry(string name, string value)
    {
        Name = name;
        Value = value;
    }
}

internal sealed class HeaderCollection : IEnumerable<HeaderEntry>
{
    private readonly List<HeaderEntry> _entries = [];

    public int Count => _entries.Count;

    public void Add(string name, string value)
    {
        _entries.Add(new HeaderEntry(name, value));
    }

    public IEnumerable<string> GetValues(string name)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return _entries[i].Value;
            }
        }
    }

    public string? GetCombined(string name)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!string.Equals(_entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sb is null)
            {
                sb = new StringBuilder(_entries[i].Value);
            }
            else
            {
                sb.Append(WellKnownHeaders.CommaSpace).Append(_entries[i].Value);
            }
        }

        return sb?.ToString();
    }

    public bool Contains(string name)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public int WireSize()
    {
        var size = 0;
        for (var i = 0; i < _entries.Count; i++)
        {
            size += _entries[i].Name.Length + 2 + _entries[i].Value.Length + 2;
        }

        size += 2; // final CRLF
        return size;
    }

    public IEnumerator<HeaderEntry> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}