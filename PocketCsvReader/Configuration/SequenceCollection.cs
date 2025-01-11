using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public class SequenceCollection : ImmutableSequenceCollection
{
    public SequenceCollection()
        : this(Empty) { }

    public SequenceCollection(ImmutableSequenceCollection sequences)
        : base(sequences.sequences) { }

    public void Add(string pattern, string? value)
    {
        var key = pattern.AsMemory();
        var val = value?.AsMemory();
        if (!sequences.TryAdd(key, val))
            sequences[key] = val;
    }

    public ImmutableSequenceCollection ToImmutable()
        => new(sequences);

    public static SequenceCollection Concat(ImmutableSequenceCollection? first, ImmutableSequenceCollection? second)
    {
        var result = new SequenceCollection(first ?? Empty);

        foreach (var (key, value) in second ?? Empty)
            result.sequences.Add(key, value);

        return result;
    }
}
