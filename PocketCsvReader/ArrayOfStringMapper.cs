using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace PocketCsvReader;

public delegate string PoolString(ReadOnlySpan<char> memory);

internal class ArrayOfStringMapper
{
    private StringMapper StringMapper { get; }

    public ArrayOfStringMapper(StringMapper stringMapper)
        => (StringMapper) = (stringMapper); 

    public string?[] Map(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans)
    {
        var fields = new string?[fieldSpans.Count()];
        var index = 0;
        foreach (var fieldSpan in fieldSpans)
            fields[index++] = StringMapper.Parse(span, fieldSpan.IsEscaped, fieldSpan.WasQuoted);
        return fields;
    }
}
