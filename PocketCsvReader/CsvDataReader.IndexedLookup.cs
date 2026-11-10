namespace PocketCsvReader;

public partial class CsvDataReader
{
    private SparseIndexSidecar.Selection? _indexedLookup;
    private FileStream? _indexedStream;
    private int _indexedOrdinal = -1;
    private int? _indexedFileStartRow;
    private FieldParsing.FieldEscaper? _indexFieldEscaper;

    internal int BatchRowNumber => HasRowNumber && _indexedFileStartRow is int start
        ? checked(start + _indexedLookup!.RowCount)
        : CurrentRowNumber;

    internal void ConfigureIndexedLookup(FileStream stream, SparseIndexSidecar.Selection lookup)
        => (_indexedStream, _indexedLookup) = (stream, lookup);

    internal int ResolveIndexOrdinal(IIndexKey key)
    {
        if (key.ColumnOrdinal is int ordinal)
            return checked(ordinal + (HasRowNumber ? 1 : 0));
        var sourceHeaders = SourceHeaders;
        var index = sourceHeaders is null ? -1 : Array.IndexOf(sourceHeaders, key.ColumnName);
        if (!Profile.Dialect.Header || index < 0)
            throw new InvalidDataException($"Indexed column '{key.ColumnName}' was not found in the CSV header.");
        return index + (HasRowNumber ? 1 : 0);
    }

    internal ReadOnlySpan<char> GetIndexKeyText(int ordinal)
    {
        var fields = CurrentFieldSpans!;
        if (ordinal >= fields.Length)
            throw new InvalidDataException($"Data record {RowCount} has no value for the indexed column.");
        var text = GetCurrentFieldMemory(ordinal).Span;
        return fields[ordinal].Value.IsEscaped ? (_indexFieldEscaper ??= new(Profile)).Escape(text) : text;
    }

    private bool ReadIndexedRow()
    {
        if (_indexedFileStartRow is null)
        {
            _indexedFileStartRow = CurrentRowNumber;
            if (_indexedLookup!.Offset is long offset)
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(CsvHeaders.Read(RecordSource!), "field_");
                else
                    RegisterHeader([new string?[_indexedLookup.FieldCount]], "field_");
                SeekIndexedRecord(offset);
            }
        }

        while (ReadRow())
        {
            var comparison = CompareIndexedRow();
            if (comparison >= 0)
                return comparison == 0;
        }
        return false;
    }

    private async ValueTask<bool> ReadIndexedRowAsync(CancellationToken cancellationToken)
    {
        if (_indexedFileStartRow is null)
        {
            _indexedFileStartRow = CurrentRowNumber;
            if (_indexedLookup!.Offset is long offset)
            {
                if (Profile.Dialect.Header)
                    RegisterHeader(await CsvHeaders.ReadAsync(RecordSource!, cancellationToken).ConfigureAwait(false), "field_");
                else
                    RegisterHeader([new string?[_indexedLookup.FieldCount]], "field_");
                SeekIndexedRecord(offset);
            }
        }

        while (await ReadRowAsync(cancellationToken).ConfigureAwait(false))
        {
            var comparison = CompareIndexedRow();
            if (comparison >= 0)
                return comparison == 0;
        }
        return false;
    }

    private int CompareIndexedRow()
    {
        var lookup = _indexedLookup!;
        if (_indexedOrdinal < 0)
            _indexedOrdinal = ResolveIndexOrdinal(lookup.Key);
        return lookup.Key.Compare(lookup.Key.Parse(GetIndexKeyText(_indexedOrdinal)), lookup.Value);
    }

    private void SeekIndexedRecord(long offset)
    {
        var stream = _indexedStream!;
        stream.Seek(offset, SeekOrigin.Begin);
        ReplaceRecordSource(CsvStreamSource.CreateAtRecord(stream, Profile));
        RowCount = _indexedLookup!.SkippedRows;
        if (HasRowNumber)
            CurrentRowNumber = checked(_indexedFileStartRow!.Value + RowCount);
    }
}
