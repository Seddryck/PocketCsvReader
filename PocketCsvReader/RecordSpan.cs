using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public readonly ref struct RecordSpan
{
    public ReadOnlySpan<char> Span { get; }
    public FieldSpan[] FieldSpans { get; }

    public RecordSpan(ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
    {
        Span = span;
        FieldSpans = fieldSpans;
    }

    public ReadOnlySpan<char> Slice(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Value.Start, FieldSpans[i].Value.Length) : throw new InvalidOperationException();

    public RecordMemory AsMemory()
        => new RecordMemory(Span, FieldSpans);
}
