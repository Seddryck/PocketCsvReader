using System;
using System.Data;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;
public class NdjsonDataReader : BaseDataReader<NdjsonProfile>
{
    private NdjsonRecordSource NdjsonRecordSource
        => (NdjsonRecordSource)RecordSource!;

    public NdjsonDataReader(Stream stream, NdjsonProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    { }

    public override int FieldCount =>
        Record?.FieldSpans.Length ?? throw new InvalidOperationException("Current record is not set.");

    public override string GetName(int i)
    {
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), i, "Field index is out of range.");
        return base.GetName(i);
    }

    public override int GetOrdinal(string name)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");

        var index = Array.IndexOf(Fields, name);
        if (index >= 0)
            return index;
        throw new ArgumentOutOfRangeException($"Field '{name}' not found.");
    }

    public override string GetRawString(int i)
    {
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i));

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

        IsEof = NdjsonRecordSource.IsEndOfFile(out var recordSpan, out var recordState);

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

    protected override IRecordSource<NdjsonProfile> CreateRecordSource(StreamReader reader, NdjsonProfile profile)
        => new NdjsonRecordSource(reader, profile);

    protected override NullableSpan GetValueOrThrow(int i)
    {
        var record = Record ?? throw new InvalidOperationException("Current record is not set.");
        if (i < record.FieldSpans.Length)
        {
            if (record.FieldSpans[i].Value.IsNull)
                return default;
            if (record.FieldSpans[i].DecodedValue is not null)
                return record.FieldSpans[i].DecodedValue.AsMemory();
            return record.Slice(i).Span;
        }
        throw new ArgumentOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {record.FieldSpans.Length} defined fields.");
    }
}
