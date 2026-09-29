namespace PocketCsvReader.KeyValue.Configuration;

public sealed class LtsvReaderBuilder : KeyValueReaderBuilder<LtsvReaderBuilder>
{
    public LtsvReaderBuilder()
        : base(KeyValueFormat.Ltsv)
    { }

    public KeyValueReader Build() => BuildReader();
}
