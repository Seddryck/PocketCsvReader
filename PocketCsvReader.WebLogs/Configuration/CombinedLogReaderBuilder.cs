using PocketCsvReader.Configuration;

namespace PocketCsvReader.WebLogs.Configuration;

public sealed class CombinedLogReaderBuilder : ReaderBuilder<CombinedLogReaderBuilder>
{
    protected override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { RowCountAtStart = false };

    public CombinedLogReader Build()
        => new(new WebLogProfile(
            WebLogFormat.Combined,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()));
}
