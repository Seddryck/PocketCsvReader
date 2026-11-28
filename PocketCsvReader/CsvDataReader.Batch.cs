using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

public partial class CsvDataReader
{
    private bool _batchWorker;
    private bool _batchHeaderRead;
    private Utf8FieldBuffer? _batchRowDecoder;
    private FieldEscaper? _batchFieldEscaper;
    private int[]? _batchSelectedOrdinals;
    private int[]? _batchPackedOrdinals;
    private int? _batchSourceFieldCount;

    private void BindBatchProjection()
    {
        _batchSelectedOrdinals = null;
        _batchPackedOrdinals = null;
        // Filters and computed fields can read source columns outside the projection.
        // Array fields keep their nested span metadata on the full-record path.
        if (_projectedOrdinals is null || Profile.Filters.Count != 0 || HasComputedFields
            || Profile.Dialect.ArrayDelimiter is not null)
            return;

        var selected = _projectedOrdinals.Distinct().Order().ToArray();
        if (selected.Length == SourceHeaders!.Length)
            return;

        _batchSelectedOrdinals = selected;
        _batchPackedOrdinals = Array.ConvertAll(_projectedOrdinals, ordinal => Array.BinarySearch(selected, ordinal));
    }

    internal long? CurrentSourceRecordNumber =>
        (RecordSource as IPositionSource)?.Tracker?.CurrentRecordNumber;

    internal async ValueTask PrepareBatchInputAsync(string[]? inheritedHeaders, bool useAsync, CancellationToken token)
    {
        _batchWorker = true;
        if (inheritedHeaders is not null)
            SetHeaders(inheritedHeaders);

        if (useAsync)
            await InitializeAsync(token).ConfigureAwait(false);
        else
            Initialize();

        if (NeedsHeader(firstRow: true))
        {
            RegisterHeader(useAsync
                ? await CsvHeaders.ReadAsync(RecordSource!, token).ConfigureAwait(false)
                : CsvHeaders.Read(RecordSource!));
            _batchHeaderRead = true;
        }
    }

    internal bool ReadBatchInput() => ReadRow();

    internal ValueTask<bool> ReadBatchInputAsync(CancellationToken token) => ReadRowAsync(token);

    internal CsvParallelRow CopyBatchInput(CsvBatchStorage storage, int inputIndex, string? fileName)
    {
        var position = RecordSource!.CurrentPosition;
        var source = new CsvRowSource(inputIndex, fileName, CurrentSourceRecordNumber ?? RowCount, position);
        var record = CopyCurrentCsvRecord(storage, _batchSelectedOrdinals, out var sourceFieldCount);
        return new(record, source, RowCount, sourceFieldCount);
    }

    internal bool LoadBatchRow(CsvParallelRow row, string[] headers, CsvBatchStorage storage, Utf8FieldBuffer decoder)
    {
        PrepareBufferedRow();
        if (!ReferenceEquals(headers, SourceHeaders))
        {
            ResetFieldCaches();
            SetHeaders(headers);
            BindBatchProjection();
        }

        _batchSourceFieldCount = row.SourceFieldCount;
        _batchRowDecoder = row.Record.Utf8 ? decoder : null;
        RowCount = row.RowCount;
        SetRecord(storage.Borrow(row.Record, decoder));
        // The worker checked the original row width before copying the payload.
        AdvanceRowNumber();
        return AcceptBufferedRow();
    }

    internal void ClearBatchRow()
    {
        PrepareBufferedRow();
        SetRecord(default(CurrentRecord));
    }
}

internal readonly record struct CsvParallelRow(CsvBatchRecord Record, CsvRowSource Source, int RowCount, int SourceFieldCount);
