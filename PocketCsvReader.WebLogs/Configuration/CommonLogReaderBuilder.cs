using PocketCsvReader.Configuration;

namespace PocketCsvReader.WebLogs.Configuration;

public sealed class CommonLogReaderBuilder : ReaderBuilder<CommonLogReaderBuilder>
{
    protected override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { RowCountAtStart = false };

    public CommonLogReader Build()
        => new(new WebLogProfile(
            WebLogFormat.Common,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()));
}
