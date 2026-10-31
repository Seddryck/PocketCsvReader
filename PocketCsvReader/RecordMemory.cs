using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public class RecordMemory
{
    private readonly ReadOnlyMemory<char> _memory;
    private readonly FieldSpan[] _fields;
    public ReadOnlyMemory<char> Span => _isUtf8 ? AsCharacters().Span : _memory;
    public FieldSpan[] FieldSpans => _isUtf8 ? AsCharacters().FieldSpans : _fields;
    internal FieldSpan[] ParsedFields => _fields;
    internal ReadOnlyMemory<char> CharacterMemoryOrDefault => _memory;
    private readonly ReadOnlyMemory<byte> _utf8;
    private readonly bool _isUtf8;
    internal bool IsUtf8 => _isUtf8;
    private readonly Utf8FieldBuffer? _decodedFields;
    private RecordMemory? _characters;

    internal bool TryGetUtf8(int ordinal, out ReadOnlyMemory<byte> value)
    {
        if (!_isUtf8)
        {
            value = default;
            return false;
        }
        var field = _fields[ordinal].Value;
        value = _utf8.Slice(field.Start, field.Length);
        return true;
    }

    internal RecordSpan AsRecordSpan() => !_isUtf8
        ? RecordSpan.FromMemory(Span, FieldSpans) : new RecordSpan(this);

    internal RecordMemory AsCharacters()
    {
        if (!_isUtf8) return this;
        if (_characters is not null) return _characters;
        var text = Encoding.UTF8.GetString(_utf8.Span);
        var fields = new FieldSpan[_fields.Length];
        int byteOffset = 0, charOffset = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            var field = _fields[i];
            var value = field.Value;
            // Parsed fields occur in input order. Count each gap/value once
            // instead of rescanning the record prefix for every column.
            // Unstarted empty fields have the default zero offset.
            int start = value.Start < byteOffset
                ? Encoding.UTF8.GetCharCount(_utf8.Span[..value.Start])
                : charOffset + Encoding.UTF8.GetCharCount(_utf8.Span.Slice(byteOffset, value.Start - byteOffset));
            int length = Encoding.UTF8.GetCharCount(_utf8.Span.Slice(value.Start, value.Length));
            fields[i] = field with { Value = value with { Start = start, Length = length } };
            if (value.Start >= byteOffset)
            {
                byteOffset = value.Start + value.Length;
                charOffset = start + length;
            }
        }
        return _characters = new RecordMemory(text.AsMemory(), fields);
    }

    internal RecordMemory(ReadOnlyMemory<byte> utf8, FieldSpan[] fields, Utf8FieldBuffer decodedFields)
    {
        _utf8 = utf8;
        _fields = fields;
        _isUtf8 = true;
        _decodedFields = decodedFields;
        decodedFields.Reset(utf8.Length, fields.Length);
    }

    private readonly static RecordMemory empty = new([], []);
    public static RecordMemory Empty { get => empty; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordMemory"/> class with the specified character data and field metadata.
    /// </summary>
    /// <param name="span">The character data representing the entire CSV record.</param>
    /// <param name="fieldSpans">An array of <see cref="FieldSpan"/> structs containing positional information for each field.</param>
    /// <remarks>
    /// Copies the characters into a new array; the field metadata array is retained without copying.
    /// Internal streaming parsers should use the memory-backed constructor or convert an existing
    /// RecordSpan view with AsMemory when the current-record lifetime permits borrowed storage.
    /// </remarks>
    public RecordMemory(ReadOnlySpan<char> span, FieldSpan[] fieldSpans)
    {
        _memory = new ReadOnlyMemory<char>(span.ToArray());
        _fields = fieldSpans;
    }

    /// <summary>
    /// Creates a view over parser-owned memory. The view is valid until the next
    /// read operation on the owning reader.
    /// </summary>
    internal RecordMemory(ReadOnlyMemory<char> memory, FieldSpan[] fieldSpans)
    {
        _memory = memory;
        _fields = fieldSpans;
    }

    /// <summary>
        /// Returns the value portion of the field at the specified index as a slice of the record's character data.
        /// </summary>
        /// <param name="i">The zero-based index of the field.</param>
        /// <returns>A <see cref="ReadOnlyMemory{char}"/> representing the field's value.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the record is empty.</exception>
        public ReadOnlyMemory<char> Slice(int i)
    {
        if (_isUtf8)
        {
            return _decodedFields!.Decode(_utf8.Span, _fields[i].Value, i);
        }
        return Span.Length > 0 ? Span.Slice(FieldSpans[i].Value.Start, FieldSpans[i].Value.Length) : throw new InvalidOperationException();
    }

    /// <summary>
        /// Returns a slice of the record's character data representing the label portion of the field at the specified index.
        /// </summary>
        /// <param name="i">The zero-based index of the field.</param>
        /// <returns>A <see cref="ReadOnlyMemory{char}"/> containing the label of the specified field.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the record is empty.</exception>
        public ReadOnlyMemory<char> SliceLabel(int i)
        => Span.Length > 0 ? Span.Slice(FieldSpans[i].Label.Start, FieldSpans[i].Label.Length) : throw new InvalidOperationException();
}
