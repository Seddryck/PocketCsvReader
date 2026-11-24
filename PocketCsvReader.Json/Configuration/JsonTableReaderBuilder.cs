using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

/// <summary>Configures a streaming reader for a top-level JSON array of row arrays.</summary>
/// <remarks>By default, the first inner array supplies the column names.</remarks>
public sealed class JsonTableReaderBuilder : ReaderBuilder<JsonTableReaderBuilder>
{
    private bool _headerRow;
    private string[]? _columnNames;
    private bool _allowComments;
    private bool _allowTrailingCommas;

    public JsonTableReaderBuilder()
        : base(JsonProfile.DefaultParserOptimizations)
    { }

    /// <summary>Uses the first inner array as the header, which is the default.</summary>
    /// <remarks>Specify either <see cref="WithHeaderRow"/> or <see cref="WithoutHeaderRow"/>, if needed.</remarks>
    public JsonTableReaderBuilder WithHeaderRow()
    {
        _headerRow = true;
        return this;
    }

    /// <summary>Uses the supplied column names and reads every inner array as a data row.</summary>
    /// <param name="columnNames">The non-null, unique column names in row order, compared ordinally.</param>
    /// <remarks>Every row must have the same number of values as the supplied column names.</remarks>
    public JsonTableReaderBuilder WithoutHeaderRow(IEnumerable<string> columnNames)
    {
        ArgumentNullException.ThrowIfNull(columnNames);
        _columnNames = columnNames.ToArray();
        return this;
    }

    /// <summary>Enables line and block comments and trailing commas.</summary>
    public JsonTableReaderBuilder WithJsonc()
        => WithComments().WithTrailingCommas();

    /// <summary>Enables or disables line and block comments independently of trailing commas.</summary>
    public JsonTableReaderBuilder WithComments(bool allowComments = true)
    {
        _allowComments = allowComments;
        return this;
    }

    /// <summary>Enables or disables trailing commas independently of comments.</summary>
    public JsonTableReaderBuilder WithTrailingCommas(bool allowTrailingCommas = true)
    {
        _allowTrailingCommas = allowTrailingCommas;
        return this;
    }

    /// <summary>Builds a JSON table reader with the configured header mode and reader settings.</summary>
    /// <exception cref="InvalidOperationException">Both header modes were selected.</exception>
    /// <exception cref="ArgumentException">A supplied column name is null or duplicated.</exception>
    public JsonTableReader Build()
    {
        if (_headerRow && _columnNames is not null)
            throw new InvalidOperationException("Choose either WithHeaderRow() or WithoutHeaderRow(columnNames).");

        var profile = new JsonProfile(
            schema: BuildSchema(),
            resource: BuildResource(),
            parsers: BuildParsers(),
            parserOptimizations: BuildParserOptimizations(),
            allowComments: _allowComments,
            allowTrailingCommas: _allowTrailingCommas)
        {
            Filters = BuildFilters(),
            Skip = Skip,
            Take = Take
        };

        return _columnNames is null
            ? new JsonTableReader(profile)
            : new JsonTableReader(profile, _columnNames);
    }
}
