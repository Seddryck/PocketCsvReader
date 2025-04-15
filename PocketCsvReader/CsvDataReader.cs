using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;
public class CsvDataReader : BaseDataReader<CsvProfile>
{
    protected new RecordParser RecordParser => (RecordParser)base.RecordParser!;

    public CsvDataReader(Stream stream, CsvProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    { }

    protected override BaseRecordParser<CsvProfile> CreateRecordParser(StreamReader reader, CsvProfile profile)
        => new RecordParser(reader, profile);

    public override int FieldCount =>
        Record?.FieldSpans.Length ?? throw new InvalidOperationException("Fields are not defined yet.");

    protected override object GetMissingField()
        => Profile.ParserOptimizations.HandleSpecialValues ? Profile.Dialect.MissingCell ?? string.Empty : string.Empty;

    public override string GetRawString(int i)
        => Record!.FieldSpans[i].Value.WasQuoted
            ? $"{Profile.Dialect.QuoteChar}{Record!.Slice(i)}{Profile.Dialect.QuoteChar}"
            : Record!.Slice(i).ToString();

    private SanitizerFactory? sanitizerFactory;
    private Dictionary<int, ISanitizer> CacheSanitizers { get; } = [];
    protected override NullableSpan GetValueOrThrow(int i)
    {
        if (i < Record!.FieldSpans.Length)
        {
            sanitizerFactory ??= new SanitizerFactory(Profile);
            var sanitizer = CacheSanitizers.GetOrAdd(i,
                sanitizerFactory.Create(SequenceCollection.Concat(Profile.Resource?.Sequences, (Profile.Schema is null ? null : GetFieldDescriptor(i))?.Sequences)
                                            , new FieldEscaper(Profile)
                ));
            return sanitizer.Sanitize(Record!.Slice(i).Span, Record!.FieldSpans[i].Value.IsEscaped, Record!.FieldSpans[i].Value.WasQuoted);
        }
        if (i < Fields!.Length && Profile.ParserOptimizations.ExtendIncompleteRecords)
            return new NullableSpan(GetMissingField().ToString().AsSpan());
        throw new ArgumentOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.FieldSpans.Length} defined fields.");
    }

    public override bool Read()
    {
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        return ReadRow();
    }

    protected virtual bool ReadRow()
    {
        var firstRow = RowCount == 0;
        if (firstRow && (Fields?.Length ?? 0) == 0 && Profile.Dialect.Header)
            RegisterHeader(RecordParser!.ReadHeaders(), "field_");

        RecordSpan rawRecord;
        RecordState state;
        do
        {
            if (IsEof)
                return false;

            IsEof = RecordParser!.IsEndOfFile(out rawRecord, out state);
            if (IsEof && (rawRecord.FieldSpans?.Length ?? 0) == 0)
            {
                Record = RecordMemory.Empty;
                return false;
            }
            RowCount++;
        } while ((Profile.Dialect.CommentRows?.Contains(RowCount) ?? false) || state == RecordState.Comment);

        if (firstRow && (Fields?.Length ?? 0) == 0)
            RegisterHeader([(string?[])Array.CreateInstance(typeof(string), rawRecord.FieldSpans!.Length)], "field_");

        Record = rawRecord.AsMemory();
        HandleUnexpectedFields(Fields!.Length);
        return true;
    }

    private void RegisterHeader(string?[][] headers, string unamedPrefix)
    {
        var maxField = headers.Select(x => x.Length).Max();
        var names = (string[])Array.CreateInstance(typeof(string), maxField);

        foreach (var header in headers)
        {
            var last = string.Empty;
            for (int i = 0; i < maxField; i++)
            {
                if (i < header.Length && !string.IsNullOrEmpty(header[i]))
                    last = header[i];
                names[i] = string.IsNullOrEmpty(names[i])
                            ? $"{last}"
                            : $"{names[i]}{Profile.Dialect.HeaderJoin}{last}";
            }
        }
        int unnamedFieldIndex = 0;
        Fields = (RecordParser!.Profile.Dialect.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{unamedPrefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{unamedPrefix}{unnamedFieldIndex++}")).ToArray();
    }

    internal void SetHeaders(string[] headers)
        => Fields = headers;

    private void HandleUnexpectedFields(int expectedLength)
    {
        var length = Record!.FieldSpans.Length;
        if (expectedLength < length)
        {
            var rowNumber = RowCount + (
                RecordParser!.Profile.Dialect.Header
                ? Math.Max(RecordParser!.Profile.Dialect.HeaderRows.Length, 1)
                : 0);
            throw new InvalidDataException
            (
                string.Format
                (
                    "The record {0} contains {1} more field{2} than expected."
                    , rowNumber
                    , length - expectedLength
                    , length - expectedLength > 1 ? "s" : string.Empty
                )
            );
        }
    }
}
