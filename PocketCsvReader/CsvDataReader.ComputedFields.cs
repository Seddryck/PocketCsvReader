namespace PocketCsvReader;

public partial class CsvDataReader
{
    private protected override bool UsesCustomComputedLayout => true;
    private protected override bool InSourceFieldView => _usingSourceFields;
    private protected override int ComputedSourceOffset => RowNumberWidth;

    protected override string GetStringValue(int i)
        => GetComputedField(i) is null ? base.GetStringValue(i) : GetString(i);
}
