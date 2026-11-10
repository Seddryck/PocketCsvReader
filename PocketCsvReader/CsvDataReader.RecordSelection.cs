namespace PocketCsvReader;

internal readonly record struct CsvRecordRange(long? Offset, int SkippedRows, int RowCount, int FieldCount);

public partial class CsvDataReader
{
    private Func<CsvDataReader, RecordSelection>? _selectRecord;
    private CsvRecordRange? _recordRange;
    private Stream? _rangeStream;
    private int? _fileStartRow;
    private FieldParsing.FieldEscaper? _fieldEscaper;

    internal int BatchRowNumber => HasRowNumber && _fileStartRow is int start && _recordRange is { } range
        ? checked(start + range.RowCount)
        : CurrentRowNumber;

    internal void SelectRecords(Func<CsvDataReader, RecordSelection> select, Stream stream, CsvRecordRange? range)
        => (_selectRecord, _rangeStream, _recordRange) = (select, stream, range);

    internal NullableSpan GetDecodedField(int ordinal)
    {
        var fields = CurrentFieldSpans!;
        if ((uint)ordinal >= (uint)fields.Length)
            return default;
        var text = GetCurrentFieldMemory(ordinal).Span;
        return fields[ordinal].Value.IsEscaped ? (_fieldEscaper ??= new(Profile)).Escape(text) : text;
    }

    private bool ReadSelectedRow()
    {
        if (_fileStartRow is null)
        {
            _fileStartRow = CurrentRowNumber;
            if (_recordRange is { Offset: long offset } range)
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(CsvHeaders.Read(RecordSource!), "field_");
                else
                    RegisterHeader([new string?[range.FieldCount]], "field_");
                SeekRecord(offset, range.SkippedRows);
            }
        }

        while (ReadRow())
        {
            var decision = _selectRecord!(this);
            if (decision != RecordSelection.Skip)
                return decision == RecordSelection.Include;
        }
        return false;
    }

    private async ValueTask<bool> ReadSelectedRowAsync(CancellationToken cancellationToken)
    {
        if (_fileStartRow is null)
        {
            _fileStartRow = CurrentRowNumber;
            if (_recordRange is { Offset: long offset } range)
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(await CsvHeaders.ReadAsync(RecordSource!, cancellationToken).ConfigureAwait(false), "field_");
                else
                    RegisterHeader([new string?[range.FieldCount]], "field_");
                SeekRecord(offset, range.SkippedRows);
            }
        }

        while (await ReadRowAsync(cancellationToken).ConfigureAwait(false))
        {
            var decision = _selectRecord!(this);
            if (decision != RecordSelection.Skip)
                return decision == RecordSelection.Include;
        }
        return false;
    }

    private void SeekRecord(long offset, int skippedRows)
    {
        var stream = _rangeStream!;
        stream.Seek(offset, SeekOrigin.Begin);
        ReplaceRecordSource(CsvStreamSource.CreateAtRecord(stream, Profile));
        RowCount = skippedRows;
        if (HasRowNumber)
            CurrentRowNumber = checked(_fileStartRow!.Value + RowCount);
    }
}
