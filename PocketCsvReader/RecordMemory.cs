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
    private string?[]? _decodedFields;
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
        var fields = _fields.Select(field => field with
        {
            Value = field.Value with
            {
                Start = Encoding.UTF8.GetCharCount(_utf8.Span[..field.Value.Start]),
                Length = Encoding.UTF8.GetCharCount(_utf8.Span.Slice(field.Value.Start, field.Value.Length))
            }
        }).ToArray();
        return _characters = new RecordMemory(text.AsMemory(), fields);
    }

    internal RecordMemory(ReadOnlyMemory<byte> utf8, FieldSpan[] fields)
    {
        _utf8 = utf8;
        _fields = fields;
        _isUtf8 = true;
    }

    private readonly static RecordMemory empty = new([], []);
    public static RecordMemory Empty { get => empty; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordMemory"/> class with the specified character data and field metadata.
    /// </summary>
    /// <param name="span">The character data representing the entire CSV record.</param>
    /// <param name="fieldSpans">An array of <see cref="FieldSpan"/> structs containing positional information for each field.</param>
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
            _decodedFields ??= new string?[_fields.Length];
            var field = _fields[i].Value;
            return (_decodedFields[i] ??= Encoding.UTF8.GetString(_utf8.Span.Slice(field.Start, field.Length))).AsMemory();
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
