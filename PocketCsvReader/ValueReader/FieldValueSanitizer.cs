namespace PocketCsvReader.FieldParsing;

internal abstract class FieldValueSanitizer : ISanitizer
{
    public NullableSpan Sanitize(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
        => Sanitize(new NullableSpan(buffer), isEscapedField, wasQuotedField);

    internal abstract NullableSpan Sanitize(NullableSpan value, bool isEscapedField, bool wasQuotedField);
}
