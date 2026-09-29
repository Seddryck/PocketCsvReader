namespace PocketCsvReader.CharParsing;

internal static class FieldParserFactory
{
    public static IParser Create(DialectDescriptor dialect)
        => dialect.QuoteChar is null && dialect.ArrayPrefix is null
            ? new UnquotedFieldParser(dialect)
            : new FieldParser(dialect);
}
