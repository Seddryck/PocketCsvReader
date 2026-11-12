namespace PocketCsvReader;

internal readonly record struct CsvRecordBlock(long Offset, int SkippedRows, int SkippedDataRows, int RowCount);

public partial class CsvDataReader
{
    private Func<CsvDataReader, RecordSelection>? _selectRecord;
    private IReadOnlyList<CsvRecordBlock>? _recordBlocks;
    private int? _indexedRowCount;
    private int _indexedFieldCount;
    private int _nextBlock;
    private int _remainingBlockRows;
    private Stream? _rangeStream;
    private int? _fileStartRow;
    private FieldParsing.FieldEscaper? _fieldEscaper;

    internal int BatchRowNumber => HasRowNumber && _fileStartRow is int start && _indexedRowCount is int count
        ? checked(start + count)
        : CurrentRowNumber;

    internal void SelectRecords(Func<CsvDataReader, RecordSelection> select) => _selectRecord = select;

    internal void SelectRecordBlocks(Func<CsvDataReader, RecordSelection> select, Stream stream,
        IReadOnlyList<CsvRecordBlock>? blocks, int rowCount, int fieldCount)
    {
        _selectRecord = select;
        _rangeStream = stream;
        _recordBlocks = blocks;
        _indexedRowCount = rowCount;
        _indexedFieldCount = fieldCount;
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
            if (_recordBlocks is { Count: > 0 })
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(CsvHeaders.Read(RecordSource!), "field_");
                else
                    RegisterHeader([new string?[_indexedFieldCount]], "field_");
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
            if (_recordBlocks is { Count: > 0 })
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(await CsvHeaders.ReadAsync(RecordSource!, cancellationToken).ConfigureAwait(false), "field_");
                else
                    RegisterHeader([new string?[_indexedFieldCount]], "field_");
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
        if (HasRowNumber && _fileStartRow is int start)
            CurrentRowNumber = checked(start + skippedDataRows);
    }
}
