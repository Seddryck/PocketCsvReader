using PocketCsvReader.Configuration;

namespace PocketCsvReader.WebLogs.Configuration;

public sealed class W3cExtendedLogReaderBuilder : ReaderBuilder<W3cExtendedLogReaderBuilder>
{
    protected override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { RowCountAtStart = false };

    public W3cExtendedLogReader Build()
        => new(new WebLogProfile(
            WebLogFormat.W3cExtended,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()));
}
