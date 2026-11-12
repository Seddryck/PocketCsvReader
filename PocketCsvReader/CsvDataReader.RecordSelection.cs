namespace PocketCsvReader;

internal readonly record struct CsvRecordRange(long? Offset, int SkippedRows, int RowCount, int FieldCount);
internal readonly record struct CsvRecordBlock(long Offset, int SkippedRows, int SkippedDataRows, int RowCount);

public partial class CsvDataReader
{
    private Func<CsvDataReader, RecordSelection>? _selectRecord;
    private CsvRecordRange? _recordRange;
    private IReadOnlyList<CsvRecordBlock>? _recordBlocks;
    private int _nextBlock;
    private int _remainingBlockRows;
    private Stream? _rangeStream;
    private int? _fileStartRow;
    private FieldParsing.FieldEscaper? _fieldEscaper;

    internal int BatchRowNumber => HasRowNumber && _fileStartRow is int start && _recordRange is { } range
        ? checked(start + range.RowCount)
        : CurrentRowNumber;

    internal void SelectRecords(Func<CsvDataReader, RecordSelection> select, Stream stream, CsvRecordRange? range)
        => (_selectRecord, _rangeStream, _recordRange) = (select, stream, range);

    internal void SelectRecords(Func<CsvDataReader, RecordSelection> select) => _selectRecord = select;

    internal void SelectRecordBlocks(Func<CsvDataReader, RecordSelection> select, Stream stream,
        IReadOnlyList<CsvRecordBlock> blocks, int rowCount, int fieldCount)
    {
        SelectRecords(select, stream, new(null, 0, rowCount, fieldCount));
        _recordBlocks = blocks;
    }

    internal NullableSpan GetDecodedField(int ordinal)
    {
        var fields = CurrentFieldSpans!;
        if ((uint)ordinal >= (uint)fields.Length)
            return default;
        var text = GetCurrentFieldMemory(ordinal).Span;
        return fields[ordinal].Value.IsEscaped ? (_fieldEscaper ??= new(Profile)).Escape(text) : text;
    }

    internal NullableSpan GetIndexField(int ordinal)
        => CurrentFieldSpans is { } fields && (uint)ordinal < (uint)fields.Length ? GetValueOrThrow(ordinal) : default;

    private bool ReadSelectedRow()
    {
        if (_fileStartRow is null)
        {
            _fileStartRow = CurrentRowNumber;
            if (NeedsSeekHeader())
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(CsvHeaders.Read(RecordSource!), "field_");
                else
                    RegisterHeader([new string?[_recordRange!.Value.FieldCount]], "field_");
                SeekSparseCheckpoint();
            }
        }

        while (PrepareSelectedRow() && ReadRow())
        {
            if (_recordBlocks is not null)
                _remainingBlockRows--;
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
            if (NeedsSeekHeader())
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(await CsvHeaders.ReadAsync(RecordSource!, cancellationToken).ConfigureAwait(false), "field_");
                else
                    RegisterHeader([new string?[_recordRange!.Value.FieldCount]], "field_");
                SeekSparseCheckpoint();
            }
        }

        while (PrepareSelectedRow() && await ReadRowAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_recordBlocks is not null)
                _remainingBlockRows--;
            var decision = _selectRecord!(this);
            if (decision != RecordSelection.Skip)
                return decision == RecordSelection.Include;
        }
        return false;
    }

    private bool NeedsSeekHeader() => _recordRange?.Offset is not null || _recordBlocks is { Count: > 0 };

    private void SeekSparseCheckpoint()
    {
        if (_recordRange is { Offset: long offset } range)
            SeekRecord(offset, range.SkippedRows, range.SkippedRows);
    }

    private bool PrepareSelectedRow()
    {
        if (_recordBlocks is null || _remainingBlockRows > 0)
            return true;
        if (_nextBlock == _recordBlocks.Count)
            return false;

        var block = _recordBlocks[_nextBlock++];
        SeekRecord(block.Offset, block.SkippedRows, block.SkippedDataRows);
        _remainingBlockRows = block.RowCount;
        return true;
    }

    private void SeekRecord(long offset, int skippedRows, int skippedDataRows)
    {
        var stream = _rangeStream!;
        stream.Seek(offset, SeekOrigin.Begin);
        ReplaceRecordSource(CsvStreamSource.CreateAtRecord(stream, Profile));
        RowCount = skippedRows;
        if (HasRowNumber)
            CurrentRowNumber = checked(_fileStartRow!.Value + skippedDataRows);
    }
}
