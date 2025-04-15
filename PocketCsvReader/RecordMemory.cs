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

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordMemory"/> class with the specified character data and field metadata.
    /// </summary>
    /// <param name="span">The character data representing the entire CSV record.</param>
    /// <param name="fieldSpans">An array of <see cref="FieldSpan"/> structs containing positional information for each field.</param>
    public RecordMemory(ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
    {
        Span = new ReadOnlyMemory<char>(span.ToArray());
        FieldSpans = fieldSpans;
    }

    /// <summary>
        /// Returns the value portion of the field at the specified index as a slice of the record's character data.
        /// </summary>
        /// <param name="i">The zero-based index of the field.</param>
        /// <returns>A <see cref="ReadOnlyMemory{char}"/> representing the field's value.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the record is empty.</exception>
        public ReadOnlyMemory<char> Slice(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Value.Start, FieldSpans[i].Value.Length) : throw new InvalidOperationException();

    /// <summary>
        /// Returns a slice of the record's character data representing the label portion of the field at the specified index.
        /// </summary>
        /// <param name="i">The zero-based index of the field.</param>
        /// <returns>A <see cref="ReadOnlyMemory{char}"/> containing the label of the specified field.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the record is empty.</exception>
        public ReadOnlyMemory<char> SliceLabel(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Label.Start, FieldSpans[i].Label.Length) : throw new InvalidOperationException();
}
