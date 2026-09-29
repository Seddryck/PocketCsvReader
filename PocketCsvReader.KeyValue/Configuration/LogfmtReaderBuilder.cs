namespace PocketCsvReader.KeyValue.Configuration;

public sealed class LogfmtReaderBuilder : KeyValueReaderBuilder<LogfmtReaderBuilder>
{
    public LogfmtReaderBuilder()
        : base(KeyValueFormat.Logfmt)
    { }

    public KeyValueReader Build() => BuildReader();
}
