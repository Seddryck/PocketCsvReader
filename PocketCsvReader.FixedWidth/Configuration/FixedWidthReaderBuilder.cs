using PocketCsvReader.Configuration;

namespace PocketCsvReader.FixedWidth.Configuration;

public sealed class FixedWidthReaderBuilder : ReaderBuilder<FixedWidthReaderBuilder>
{
    private readonly List<FixedWidthFieldDescriptor> _fields = [];
    private string _lineTerminator = Environment.NewLine;
    private bool _header;
    private bool _allowTrailingCharacters;

    public FixedWidthReaderBuilder WithField(
        string name,
        int offset,
        int width,
        FixedWidthPadding padding = FixedWidthPadding.Right,
        char paddingChar = ' ')
    {
        _fields.Add(new(name, offset, width, padding, paddingChar));
        return this;
    }

    public FixedWidthReaderBuilder WithLineTerminator(string lineTerminator)
    {
        _lineTerminator = lineTerminator;
        return this;
    }

    public FixedWidthReaderBuilder WithHeader(bool header = true)
    {
        _header = header;
        return this;
    }

    public FixedWidthReaderBuilder AllowTrailingCharacters(bool allow = true)
    {
        _allowTrailingCharacters = allow;
        return this;
    }

    protected override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { RowCountAtStart = false };

    public FixedWidthReader Build()
    {
        var descriptor = new FixedWidthDescriptor(
            _fields.ToArray(),
            _lineTerminator,
            _header,
            _allowTrailingCharacters);
        var profile = new FixedWidthProfile(
            descriptor,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations());
        return new FixedWidthReader(profile);
    }
}
