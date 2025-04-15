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

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordSpan"/> struct with the specified character span and field spans.
    /// </summary>
    /// <param name="span">The span of characters representing the entire CSV record.</param>
    /// <param name="fieldSpans">An array of <see cref="FieldSpan"/> structs indicating the positions and lengths of each field within the record.</param>
    public RecordSpan(ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
    {
        Span = span;
        FieldSpans = fieldSpans;
    }

    /// <summary>
        /// Returns a span representing the characters of the specified field within the record.
        /// </summary>
        /// <param name="i">The zero-based index of the field to extract.</param>
        /// <returns>A <see cref="ReadOnlySpan{char}"/> containing the field's characters.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the record span is empty.</exception>
        public ReadOnlySpan<char> Slice(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Value.Start, FieldSpans[i].Value.Length) : throw new InvalidOperationException();

    /// <summary>
        /// Converts the current record span to a <see cref="RecordMemory"/> instance representing the same data.
        /// </summary>
        /// <returns>A <see cref="RecordMemory"/> containing the record's character span and field spans.</returns>
        public RecordMemory AsMemory()
        => new RecordMemory(Span, FieldSpans);
}
