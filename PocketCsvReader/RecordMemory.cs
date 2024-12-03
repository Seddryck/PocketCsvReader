using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public class RecordMemory
{
    public ReadOnlyMemory<char> Span { get; }
    public FieldSpan[] FieldSpans { get; }

    private readonly static RecordMemory empty = new([], []);
    public static RecordMemory Empty { get => empty; }

    public RecordMemory(ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
    {
        Span = new ReadOnlyMemory<char>(span.ToArray());
        FieldSpans = fieldSpans;
    }

    public ReadOnlyMemory<char> Slice(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Start, FieldSpans[i].Length) : throw new InvalidOperationException();
}
