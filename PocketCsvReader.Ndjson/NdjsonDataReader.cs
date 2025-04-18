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
        int index = Fields is null ? -1 : Array.IndexOf(Fields!, name);
        if (index >= 0)
            return index;
        index = Fields?.Length ?? 0;
        var list = new List<string>(Fields ?? Array.Empty<string>());
        do
        {
            var fieldName = Record!.SliceLabel(index).ToString();
            list.Add(fieldName);
            if (fieldName == name)
            {
                Fields = [.. list];
                return index;
            }
            index += 1;
        } while (index < Record!.FieldSpans.Length);
        Fields = [.. list];
        throw new ArgumentOutOfRangeException($"Field '{name}' not found.");
    }

    public override string GetRawString(int i)
    {
        var addChar = Record!.FieldSpans[i].Label.WasQuoted ? 1 : 0;
        return Record!.Span.Slice(Record!.FieldSpans[i].Label.Start, Record!.FieldSpans[i].Value.Length + addChar).ToString();
    }

    public override bool Read()
    {
        Fields = [];

        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        IsEof = RecordParser!.IsEndOfFile(out var recordSpan, out _);

        if (recordSpan.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }
        else
            Record = recordSpan.AsMemory();

        RowCount++;

        return true;
    }

    protected override BaseRecordParser<NdjsonProfile> CreateRecordParser(StreamReader reader, NdjsonProfile profile)
        => new RecordParser(reader, profile);

    protected override NullableSpan GetValueOrThrow(int i)
    {
        if (i < Record!.FieldSpans.Length)
        {
            return Record!.Slice(i).Span;
        }
        throw new ArgumentOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.FieldSpans.Length} defined fields.");
    }
}
