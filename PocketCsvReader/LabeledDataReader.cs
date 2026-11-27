using System.Data;
using System.Runtime.CompilerServices;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

/// <summary>
/// Provides common data-reader behavior for formats whose records carry a label alongside every value.
/// </summary>
public abstract class LabeledDataReader<TProfile> : BaseDataReader<TProfile>
    where TProfile : IProfile
{
    private readonly LabelRowShapeCache _rowShapeCache = new();
    private LabelRowShape _rowShape = LabelRowShape.Empty;

    protected LabeledDataReader(Stream stream, TProfile profile, StringMapper stringMapper)
        : base(stream, profile, stringMapper)
    { }

    protected virtual bool HasStableLabelShape => false;

    public override int FieldCount =>
        CurrentFieldSpans?.Length ?? throw new InvalidOperationException("Current record is not set.");

    public override string GetName(int i)
    {
        ValidateOrdinal(i);
        return base.GetName(i);
    }

    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");
        if (_rowShape.TryGetOrdinal(name, out var index))
            return index;
        throw new ArgumentOutOfRangeException(nameof(name), $"Field '{name}' not found.");
    }

    public override bool TryGetOrdinal(string name, out int? ordinal)
    {
        if (Fields is not null && _rowShape.TryGetOrdinal(name, out var matchingOrdinal))
        {
            ordinal = matchingOrdinal;
            return true;
        }

        ordinal = null;
        return false;
    }

    public override string GetRawString(int i)
    {
        ValidateOrdinal(i);

        var fields = CurrentFieldSpans ?? throw new InvalidOperationException("Current record is not set.");
        var value = fields[i].Value;
        var quoteLength = value.WasQuoted ? 1 : 0;
        return CurrentRecordMemory.Slice(value.Start - quoteLength, value.Length + (quoteLength * 2)).ToString();
    }

    protected override object GetNullValue(int i)
        => DBNull.Value;

    protected override bool ReadCore()
    {
        Fields = [];

        if (CurrentRecordSource is { } source)
            return CompleteBorrowedRead(source.ReadCurrent());

        IsEof = RecordSource!.IsEndOfFile(out var record, out var state);
        SetRecord(record.Memory, record.FieldSpans);
        return CompleteRead(state, record.Memory, record.FieldSpans);
    }

    protected override ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
        => CurrentRecordSource is { } current
            ? ReadBorrowedCoreAsync(current, cancellationToken)
            : ReadPublicCoreAsync(cancellationToken);

    private async ValueTask<bool> ReadBorrowedCoreAsync(ICurrentRecordSource source, CancellationToken cancellationToken)
    {
        Fields = [];
        var result = await source.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        return CompleteBorrowedRead(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CompleteBorrowedRead(in CurrentRecordResult result)
    {
        var record = result.Record;
        IsEof = result.IsEndOfFile;
        SetRecord(record, copyMetadataOnMaterialization: true);
        return CompleteRead(result.State, record.Characters, record.Fields!);
    }

    // Separate state machines keep the public-source path's awaiter small and
    // avoid a forwarding allocation whenever an external source suspends.
    private async ValueTask<bool> ReadPublicCoreAsync(CancellationToken cancellationToken)
    {
        Fields = [];
        var result = await RecordSource!.ReadAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
        Record = result.Record;
        return CompleteRead(result.State, result.Record.Span, result.Record.FieldSpans);
    }

    private bool CompleteRead(RecordState state, ReadOnlyMemory<char> record, FieldSpan[] fields)
    {
        if (state == RecordState.Eof)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        if (!HasStableLabelShape || RowCount == 0)
            UpdateRowShape(record, fields);
        Fields = _rowShape.Labels;

        RowCount++;
        return true;
    }

    private void UpdateRowShape(ReadOnlyMemory<char> record, FieldSpan[] fields)
    {
        var shape = _rowShapeCache.Resolve(record, fields, _rowShape);
        if (!ReferenceEquals(_rowShape, shape))
        {
            ResetFieldCaches();
            StringMapper.ClearLastValueHints();
        }
        _rowShape = shape;
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        var fields = CurrentFieldSpans ?? throw new InvalidOperationException("Current record is not set.");
        if (i < 0 || i >= fields.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(i),
                $"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {fields.Length} defined fields.");
        }

        ref var field = ref fields[i];
        if (field.Value.IsNull)
            return default;
        return GetFieldMemory(ref field);
    }

    private void ValidateOrdinal(int i)
    {
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), i, "Field index is out of range.");
    }
}
