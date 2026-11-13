using PocketCsvReader.Configuration;

namespace PocketCsvReader.Markdown.Configuration;

public sealed class MarkdownReaderBuilder : ReaderBuilder<MarkdownReaderBuilder>
{
    private int _skipTables;
    private string? _textMarker;
    private BadDataPolicy? _badDataPolicy;

    /// <summary>Skips the specified number of valid tables before selecting a table.</summary>
    public MarkdownReaderBuilder WithSkipTables(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _skipTables = count;
        return this;
    }

    /// <summary>Selects the table beginning on the next nonblank line after the first matching marker.</summary>
    public MarkdownReaderBuilder WithTableAfter(string textMarker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textMarker);
        _textMarker = textMarker;
        return this;
    }

    /// <summary>Sets how data rows containing surplus cells are handled.</summary>
    public MarkdownReaderBuilder WithBadDataPolicy(BadDataPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _badDataPolicy = policy;
        return this;
    }

    public MarkdownReader Build()
        => new(new MarkdownProfile(_skipTables, _textMarker,
            BuildSchema(), BuildResource(), BuildParsers(), BuildParserOptimizations())
        {
            Filters = BuildFilters(),
            Skip = Skip,
            Take = Take,
            BadDataPolicy = _badDataPolicy
        });
}
