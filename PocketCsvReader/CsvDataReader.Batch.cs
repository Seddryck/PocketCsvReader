using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

public partial class CsvDataReader
{
    private bool _batchWorker;
    private bool _batchHeaderRead;
    private Utf8FieldBuffer? _batchRowDecoder;
    private FieldEscaper? _batchFieldEscaper;

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

    internal CsvParallelRow CopyBatchInput(int inputIndex, string? fileName)
    {
        var position = RecordSource!.CurrentPosition;
        var source = new CsvRowSource(inputIndex, fileName, CurrentSourceRecordNumber ?? RowCount, position);
        return new(CopyCurrentCsvRecord(), source, RowCount);
    }

    internal bool LoadBatchRow(CsvParallelRow row, string[] headers, Utf8FieldBuffer decoder)
    {
        PrepareBufferedRow();
        if (!ReferenceEquals(headers, SourceHeaders))
        {
            ResetFieldCaches();
            SetHeaders(headers);
        }

        _batchRowDecoder = row.Record.Bytes is not null ? decoder : null;
        RowCount = row.RowCount;
        SetRecord(row.Record.Borrow(decoder));
        CompleteRecord(row.Record.FieldCount);
        return AcceptBufferedRow();
    }

    internal void ClearBatchRow()
    {
        PrepareBufferedRow();
        SetRecord(default(CurrentRecord));
    }
}

internal readonly record struct CsvParallelRow(OwnedCsvRecord Record, CsvRowSource Source, int RowCount);
