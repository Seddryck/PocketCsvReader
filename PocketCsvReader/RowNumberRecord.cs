using System.Globalization;

namespace PocketCsvReader;

/// <summary>Owns the augmented record until the reader advances.</summary>
internal sealed class RowNumberRecord
{
    private char[] _buffer = [];

    internal RecordMemory Prepend(RecordMemory source, int number, char delimiter)
    {
        // SpanMapper requires all fields to point into one character buffer.
        // Reuse that buffer, and leave the parser's source memory untouched.
        var record = source.AsCharacters();
        var required = checked(record.Span.Length + 11);
        if (_buffer.Length < required)
            Array.Resize(ref _buffer, Math.Max(required, _buffer.Length * 2));
        number.TryFormat(_buffer, out var length, provider: CultureInfo.InvariantCulture);
        _buffer[length] = delimiter;
        var offset = length + 1;
        record.Span.Span.CopyTo(_buffer.AsSpan(offset));

        var fields = new FieldSpan[record.FieldSpans.Length + 1];
        fields[0] = new FieldSpan(0, length);
        for (var i = 0; i < record.FieldSpans.Length; i++)
            fields[i + 1] = Shift(record.FieldSpans[i], offset);
        return new RecordMemory(_buffer.AsMemory(0, offset + record.Span.Length), fields);
    }

    private static FieldSpan Shift(FieldSpan field, int offset)
        => field with
        {
            Value = field.Value with { Start = field.Value.Start + offset },
            Label = field.Label with { Start = field.Label.Start + offset },
            Children = field.Children?.Select(child => Shift(child, offset)).ToArray()
        };
}
