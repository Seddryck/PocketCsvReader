using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Json;

internal static class JsonValueDecoder
{
    public static bool TryMapString(ReadOnlyMemory<char> record, FieldSpan[]? fields, int ordinal,
        StringMapper mapper, out string value)
    {
        value = null!;
        if (fields is null || (uint)ordinal >= (uint)fields.Length)
            return false;

        ref var field = ref fields[ordinal];
        var source = field.Value;
        if (source.IsNull)
            return false;

        if (field.DecodedValue is { } decoded)
        {
            value = mapper.Map(ordinal, decoded.AsSpan());
            return true;
        }

        var raw = record.Span.Slice(source.Start, source.Length);
        if (source is not { WasQuoted: true, IsEscaped: true })
        {
            value = mapper.Map(ordinal, raw);
            return true;
        }

        // A single decoded UTF-16 code unit can take up to six characters on the wire.
        if (source.Length > 6 * StringMapper.MaxHintedFieldLength)
            return false;

        Span<char> scratch = stackalloc char[source.Length];
        int length = JsonRecordParser.DecodeEscapedString(raw, scratch);
        var text = scratch[..length];
        value = mapper.Map(ordinal, text);
        // A custom pool may transform text. Only store the logical JSON value in the field metadata.
        if (mapper.UsesDefaultPool || text.SequenceEqual(value.AsSpan()))
            field.DecodedValue = value;
        return true;
    }

    public static ReadOnlyMemory<char> GetMemory(
        ReadOnlyMemory<char> record, ref FieldSpan field, ref JsonFieldBuffer? buffer)
    {
        if (field.DecodedValue is null && field.Value is { WasQuoted: true, IsEscaped: true })
        {
            buffer ??= new JsonFieldBuffer();
            field.DecodedValue = JsonRecordParser.DecodeString(
                record.Span.Slice(field.Value.Start - 1, field.Value.Length + 2), buffer);
        }
        return field.DecodedValue is { } decoded
            ? decoded.AsMemory()
            : record.Slice(field.Value.Start, field.Value.Length);
    }
}
