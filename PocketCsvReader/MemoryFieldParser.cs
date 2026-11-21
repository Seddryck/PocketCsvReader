namespace PocketCsvReader;

internal delegate T ParseMemory<T>(ReadOnlyMemory<char> memory);

internal abstract class MemoryFieldParser
{
    internal abstract ParseMemory<object> Boxed { get; }
    internal abstract Delegate Identity { get; }

    internal bool IsValid(ReadOnlyMemory<char> memory)
    {
        try
        {
            _ = Boxed(memory);
            return true;
        }
        catch (Exception ex) when (SpanParser.IsConversionFailure(ex))
        {
            return false;
        }
    }
}

internal sealed class MemoryFieldParser<T>(ParseMemory<T> parse, ParseMemory<object> boxed) : MemoryFieldParser
{
    internal ParseMemory<T> Parse { get; } = parse;
    internal override ParseMemory<object> Boxed { get; } = boxed;
    internal override Delegate Identity => Parse;
}
