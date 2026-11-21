namespace PocketCsvReader.NetTopologySuite;

// The WKT tokenizer consumes this reader synchronously while the field memory is borrowed.
internal sealed class MemoryTextReader(ReadOnlyMemory<char> text) : TextReader
{
    private int _position;

    public override int Peek() => _position < text.Length ? text.Span[_position] : -1;

    public override int Read() => _position < text.Length ? text.Span[_position++] : -1;
}
