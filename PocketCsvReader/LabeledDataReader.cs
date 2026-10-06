using System.Data;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

/// <summary>
/// Provides common data-reader behavior for formats whose records carry a label alongside every value.
/// </summary>
public abstract class LabeledDataReader<TProfile> : BaseDataReader<TProfile>
    where TProfile : IProfile
{
    protected LabeledDataReader(Stream stream, TProfile profile, StringMapper stringMapper)
        : base(stream, profile, stringMapper)
    { }

    public override int FieldCount =>
        Record?.FieldSpans.Length ?? throw new InvalidOperationException("Current record is not set.");

    public override string GetName(int i)
    {
        ValidateOrdinal(i);
        return base.GetName(i);
    }

    public override int GetOrdinal(string name)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");

        var index = Array.IndexOf(Fields, name);
        if (index >= 0)
            return index;
        throw new ArgumentOutOfRangeException(nameof(name), $"Field '{name}' not found.");
    }

    public override string GetRawString(int i)
    {
        ValidateOrdinal(i);

        var record = Record ?? throw new InvalidOperationException("Current record is not set.");
        var value = record.FieldSpans[i].Value;
        var quoteLength = value.WasQuoted ? 1 : 0;
        return record.Span.Slice(value.Start - quoteLength, value.Length + (quoteLength * 2)).ToString();
    }

    protected override object GetNullValue(int i)
        => DBNull.Value;

    protected override bool ReadCore()
    {
        Fields = [];

        IsEof = RecordSource!.IsEndOfFile(out var recordSpan, out var recordState);
        if (recordState == RecordState.Eof)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        Record = recordSpan.AsMemory();
        Fields = new string[recordSpan.FieldSpans.Length];
        for (var index = 0; index < recordSpan.FieldSpans.Length; index++)
        {
            var field = recordSpan.FieldSpans[index];
            Fields[index] = field.DecodedLabel
                ?? recordSpan.Span.Slice(field.Label.Start, field.Label.Length).ToString();
        }

        RowCount++;
        return true;
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        Fields = [];

        var result = await RecordSource!.ReadAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
        if (result.State == RecordState.Eof)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        Record = result.Record;
        Fields = new string[result.Record.FieldSpans.Length];
        for (var index = 0; index < result.Record.FieldSpans.Length; index++)
        {
            var field = result.Record.FieldSpans[index];
            Fields[index] = field.DecodedLabel
                ?? result.Record.Span.Slice(field.Label.Start, field.Label.Length).ToString();
        }

        RowCount++;
        return true;
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        var record = Record ?? throw new InvalidOperationException("Current record is not set.");
        if (i < 0 || i >= record.FieldSpans.Length)
            throw new ArgumentOutOfRangeException(nameof(i),
                $"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {record.FieldSpans.Length} defined fields.");

        var field = record.FieldSpans[i];
        if (field.Value.IsNull)
            return default;
        if (field.DecodedValue is not null)
            return field.DecodedValue.AsMemory();
        return record.Slice(i).Span;
    }

    private void ValidateOrdinal(int i)
    {
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), i, "Field index is out of range.");
    }
}
