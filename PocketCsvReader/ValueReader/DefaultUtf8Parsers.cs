namespace PocketCsvReader.ValueReader;

internal static class DefaultUtf8Parsers
{
    private static readonly Dictionary<Type, Delegate> Parsers = new()
    {
        [typeof(short)] = (TryParseUtf8<short>)Int16Utf8Parser.TryParse,
        [typeof(int)] = (TryParseUtf8<int>)Int32Utf8Parser.TryParse,
        [typeof(long)] = (TryParseUtf8<long>)Int64Utf8Parser.TryParse,
        [typeof(bool)] = (TryParseUtf8<bool>)BooleanUtf8Parser.TryParse,
        [typeof(float)] = (TryParseUtf8<float>)SingleUtf8Parser.TryParse,
        [typeof(double)] = (TryParseUtf8<double>)DoubleUtf8Parser.TryParse,
        [typeof(decimal)] = (TryParseUtf8<decimal>)InvariantDecimalUtf8Parser.TryParse,
        [typeof(DateOnly)] = (TryParseUtf8<DateOnly>)IsoDateOnlyUtf8Parser.TryParse,
        [typeof(TimeOnly)] = (TryParseUtf8<TimeOnly>)TimeOnlyUtf8Parser.TryParse,
    };

    // Resolve built-ins once per closed type, including types without a byte parser.
    internal static class For<T>
    {
        internal static readonly TryParseUtf8<T>? Parser = (TryParseUtf8<T>?)Parsers.GetValueOrDefault(typeof(T));
    }
}
