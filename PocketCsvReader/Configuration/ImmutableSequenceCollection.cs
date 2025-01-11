using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class ImmutableSequenceCollection : IEnumerable<KeyValuePair<ReadOnlyMemory<char>, ReadOnlyMemory<char>?>>
{
    private class ReadOnlyMemoryComparer : IEqualityComparer<ReadOnlyMemory<char>>
    {
        public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
            => x.Span.SequenceEqual(y.Span);

        public int GetHashCode(ReadOnlyMemory<char> obj)
            => obj.Length;
    }

    protected internal readonly Dictionary<ReadOnlyMemory<char>, ReadOnlyMemory<char>?> sequences = [];

    protected ImmutableSequenceCollection()
    { }

    protected internal ImmutableSequenceCollection(Dictionary<ReadOnlyMemory<char>, ReadOnlyMemory<char>?> sequences)
        => this.sequences = new(sequences, new ReadOnlyMemoryComparer());

    public bool TryGetValue(ReadOnlySpan<char> key, out ReadOnlyMemory<char>? value)
        => sequences.TryGetValue(key.ToArray().AsMemory(), out value);

    public IEnumerator<KeyValuePair<ReadOnlyMemory<char>, ReadOnlyMemory<char>?>> GetEnumerator()
        => sequences.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public static readonly ImmutableSequenceCollection Empty = new();

    public bool IsEmpty => sequences.Count == 0;
}
