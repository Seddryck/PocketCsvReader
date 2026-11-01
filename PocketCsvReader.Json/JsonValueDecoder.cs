namespace PocketCsvReader.Json;

internal static class JsonValueDecoder
{
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
