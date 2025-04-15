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

    /// <summary>
        /// Returns the default value for a missing CSV field, using the profile's configured missing cell value if special value handling is enabled; otherwise, returns an empty string.
        /// </summary>
        protected override object GetMissingField()
        => Profile.ParserOptimizations.HandleSpecialValues ? Profile.Dialect.MissingCell ?? string.Empty : string.Empty;

    /// <summary>
            /// Returns the raw string representation of the field at the specified index, including surrounding quotes if the field was quoted in the original CSV.
            /// </summary>
            /// <param name="i">The zero-based index of the field.</param>
            /// <returns>The raw field string, wrapped in quote characters if originally quoted; otherwise, the field value as a string.</returns>
            public override string GetRawString(int i)
        => Record!.FieldSpans[i].Value.WasQuoted
            ? $"{Profile.Dialect.QuoteChar}{Record!.Slice(i)}{Profile.Dialect.QuoteChar}"
            : Record!.Slice(i).ToString();

    private SanitizerFactory? sanitizerFactory;
    private Dictionary<int, ISanitizer> CacheSanitizers { get; } = [];
    /// <summary>
    /// Returns the sanitized value of the field at the specified index, applying field-specific sanitization and handling missing or incomplete fields according to the CSV profile.
    /// </summary>
    /// <param name="i">The zero-based index of the field to retrieve.</param>
    /// <returns>A <see cref="NullableSpan"/> containing the sanitized field value, or a missing field value if the index is within the expected range and incomplete record extension is enabled.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the field index is outside the range of available and expected fields.</exception>
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
            return new NullableSpan(Profile.ParserOptimizations.HandleSpecialValues ? Profile.Dialect.MissingCell ?? string.Empty : string.Empty);
        throw new ArgumentOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.FieldSpans.Length} defined fields.");
    }

    /// <summary>
    /// Advances the reader to the next CSV record.
    /// </summary>
    /// <returns><c>true</c> if a new record was read; <c>false</c> if the end of the file has been reached.</returns>
    public override bool Read()
    {
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        return ReadRow();
    }

    /// <summary>
    /// Reads the next non-comment CSV row, handling headers and end-of-file conditions as needed.
    /// </summary>
    /// <returns>True if a data row was successfully read; false if end-of-file is reached.</returns>
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

    /// <summary>
    /// Throws an exception if the current record contains more fields than the expected count.
    /// </summary>
    /// <param name="expectedLength">The expected number of fields for the current record.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the record has more fields than expected, indicating the row number and the number of extra fields.
    /// </exception>
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
