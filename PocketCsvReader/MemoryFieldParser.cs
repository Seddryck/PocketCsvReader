namespace PocketCsvReader;

internal delegate T ParseMemory<T>(ReadOnlyMemory<char> memory);

internal abstract class MemoryFieldParser
{
    private ParseMemory<object>? _boxed;
    internal ParseMemory<object> Boxed => _boxed ??= ParseBoxed;
    internal abstract Delegate Identity { get; }

    protected abstract object ParseBoxed(ReadOnlyMemory<char> memory);

    internal bool IsValid(ReadOnlyMemory<char> memory)
    {
        try
        {
            _ = ParseBoxed(memory);
            return true;
        }
        catch (Exception ex) when (SpanParser.IsConversionFailure(ex))
        {
            return false;
        }
    }
}

internal sealed class MemoryFieldParser<T>(ParseMemory<T> parse) : MemoryFieldParser
{
    internal ParseMemory<T> Parse { get; } = parse;
    internal override Delegate Identity => Parse;
    protected override object ParseBoxed(ReadOnlyMemory<char> memory) => Parse(memory)!;
}
