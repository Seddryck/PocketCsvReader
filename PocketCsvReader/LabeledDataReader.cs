using System.Data;
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
        if (!HasStableLabelShape || RowCount == 0)
            _rowShape = LabelRowShape.Empty;

        IsEof = RecordSource!.IsEndOfFile(out var recordSpan, out var recordState);
        if (recordState == RecordState.Eof)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        SetRecord(recordSpan.Memory, recordSpan.FieldSpans);
        if (!HasStableLabelShape || RowCount == 0)
            _rowShape = _rowShapeCache.Resolve(recordSpan.Memory, recordSpan.FieldSpans);
        Fields = _rowShape.Labels;

        RowCount++;
        return true;
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        Fields = [];
        if (!HasStableLabelShape || RowCount == 0)
            _rowShape = LabelRowShape.Empty;

        var result = await RecordSource!.ReadAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
        if (result.State == RecordState.Eof)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        Record = result.Record;
        if (!HasStableLabelShape || RowCount == 0)
            _rowShape = _rowShapeCache.Resolve(result.Record.Span, result.Record.FieldSpans);
        Fields = _rowShape.Labels;

        RowCount++;
        return true;
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        var fields = CurrentFieldSpans ?? throw new InvalidOperationException("Current record is not set.");
        if (i < 0 || i >= fields.Length)
            throw new ArgumentOutOfRangeException(nameof(i),
                $"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {fields.Length} defined fields.");

        var field = fields[i];
        if (field.Value.IsNull)
            return default;
        if (field.DecodedValue is not null)
            return field.DecodedValue.AsMemory();
        return CurrentRecordMemory.Span.Slice(field.Value.Start, field.Value.Length);
    }

    private void ValidateOrdinal(int i)
    {
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), i, "Field index is out of range.");
    }
}
