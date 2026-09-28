using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;
public class NdjsonDataReader : BaseDataReader<NdjsonProfile>
{
    public NdjsonDataReader(Stream stream, NdjsonProfile profile)
        : base(stream, profile, new StringMapper())
    { }

    public override int FieldCount =>
        Record?.FieldSpans.Length ?? throw new InvalidOperationException("Current record is not set.");

    public override int GetOrdinal(string name)
    {
        int index = Fields is null ? -1 : Array.IndexOf(Fields, name);
        if (index >= 0)
            return index;
        index = Fields?.Length ?? 0;
        var list = new List<string>(Fields ?? Array.Empty<string>());
        var record = Record ?? throw new InvalidOperationException("Current record is not set.");
        do
        {
            var fieldName = record.FieldSpans[index].DecodedLabel ?? record.SliceLabel(index).ToString();
            list.Add(fieldName);
            if (fieldName == name)
            {
                Fields = [.. list];
                return index;
            }
            index += 1;
        } while (index < record.FieldSpans.Length);
        Fields = [.. list];
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

    public override object GetValue(int i)
        => IsDBNull(i) ? DBNull.Value : base.GetValue(i);

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var length = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < length; i++)
            values[i] = GetValue(i);
        return length;
    }

    public override bool Read()
    {
        Fields = [];

        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        var parser = RecordParser ?? throw new InvalidOperationException("Record parser is not initialized.");
        IsEof = parser.IsEndOfFile(out var recordSpan, out var recordState);

        if (recordState == RecordState.Eof)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        Record = recordSpan.AsMemory();

        RowCount++;

        return true;
    }

    protected override BaseRecordParser<NdjsonProfile> CreateRecordParser(StreamReader reader, NdjsonProfile profile)
        => new RecordParser(reader, profile);

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
