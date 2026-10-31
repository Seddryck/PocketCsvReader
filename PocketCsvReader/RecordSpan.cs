using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public readonly ref struct RecordSpan
{
    private readonly ReadOnlyMemory<char> _memory;
    private readonly RecordMemory? _utf8Record;
    public ReadOnlySpan<char> Span => _utf8Record is null ? _memory.Span : _utf8Record.Span.Span;
    internal ReadOnlyMemory<char> Memory => _utf8Record?.Span ?? _memory;
    private readonly FieldSpan[] _fields;
    public FieldSpan[] FieldSpans => _utf8Record?.FieldSpans ?? _fields;
    internal FieldSpan[] ParsedFields => _fields;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordSpan"/> struct with the specified character span and field spans.
    /// </summary>
    /// <param name="span">The span of characters representing the entire CSV record.</param>
    /// <param name="fieldSpans">An array of <see cref="FieldSpan"/> structs indicating the positions and lengths of each field within the record.</param>
    /// <remarks>
    /// Copies the characters into a new array; the field metadata array is retained without copying.
    /// Internal streaming parsers with an established memory lifetime should use FromMemory instead
    /// to avoid copying an already buffered record.
    /// </remarks>
    public RecordSpan(ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
        : this(new ReadOnlyMemory<char>(span.ToArray()), fieldSpans)
    { }

    private RecordSpan(ReadOnlyMemory<char> memory, FieldSpan[] fieldSpans)
    {
        _memory = memory;
        _utf8Record = null;
        _fields = fieldSpans;
    }

    internal RecordSpan(RecordMemory record)
    {
        _memory = default;
        _utf8Record = record;
        _fields = record.ParsedFields;
    }

    internal static RecordSpan FromMemory(ReadOnlyMemory<char> memory, FieldSpan[] fieldSpans)
        => new(memory, fieldSpans);

    /// <summary>
        /// Returns a span representing the characters of the specified field within the record.
        /// </summary>
        /// <param name="i">The zero-based index of the field to extract.</param>
        /// <returns>A <see cref="ReadOnlySpan{char}"/> containing the field's characters.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the record span is empty.</exception>
        public ReadOnlySpan<char> Slice(int i)
        => _utf8Record is not null ? _utf8Record.Slice(i).Span
            : Span.Length > 0 ? Span.Slice(FieldSpans[i].Value.Start, FieldSpans[i].Value.Length) : throw new InvalidOperationException();

    /// <summary>
        /// Converts the current record span to a <see cref="RecordMemory"/> instance representing the same data.
        /// </summary>
        /// <returns>A <see cref="RecordMemory"/> containing the record's character span and field spans.</returns>
    public RecordMemory AsMemory()
        => _utf8Record ?? new RecordMemory(_memory, FieldSpans);
}
