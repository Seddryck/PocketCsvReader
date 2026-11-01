using PocketCsvReader.Configuration;

namespace PocketCsvReader.SecurityLogs.Configuration;

public abstract class SecurityLogReaderBuilder<TBuilder> : ReaderBuilder<TBuilder>
    where TBuilder : SecurityLogReaderBuilder<TBuilder>
{
    private string[]? _attributes;

    /// <summary>Selects attribute columns in order after the fixed header columns.</summary>
    public TBuilder WithAttributes(params string[] attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        _attributes = (string[])attributes.Clone();
        return (TBuilder)this;
    }

    protected override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { RowCountAtStart = false };

    protected SecurityLogProfile BuildProfile(SecurityLogFormat format)
        => new(format, _attributes, BuildSchema(), BuildResource(), BuildParsers(), BuildParserOptimizations());
}

public sealed class CefReaderBuilder : SecurityLogReaderBuilder<CefReaderBuilder>
{
    public CefReader Build() => new(BuildProfile(SecurityLogFormat.Cef));
}

public sealed class LeefReaderBuilder : SecurityLogReaderBuilder<LeefReaderBuilder>
{
    public LeefReader Build() => new(BuildProfile(SecurityLogFormat.Leef));
}
