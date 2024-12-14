using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public readonly ref struct RecordSpan
{
    public CsvProfile Profile { get; }
    public ReadOnlySpan<char> Span { get; }
    public FieldSpan[] FieldSpans { get; }

    public RecordSpan(CsvProfile profile, ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
    {
        Profile = profile;
        Span = span;
        FieldSpans = fieldSpans;
    }

    public ReadOnlySpan<char> Slice(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Start, FieldSpans[i].Length) : throw new InvalidOperationException();

    public RecordMemory AsMemory()
        => new RecordMemory(Span, FieldSpans);
}
