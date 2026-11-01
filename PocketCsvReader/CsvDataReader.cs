using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;
public class CsvDataReader : BaseDataReader<CsvProfile>
{
    private readonly RowNumberOptions? _rowNumberOptions;
    private readonly RowNumberRecord? _rowNumberRecord;
    private static readonly FieldDescriptor RowNumberField = new(typeof(int), DataSourceTypeName: "integer");
    internal bool HasRowNumber => _rowNumberOptions is not null;
    internal int CurrentRowNumber { get; set; }

    protected new RecordParser RecordParser => (RecordParser)base.RecordParser!;

    public CsvDataReader(Stream stream, CsvProfile profile)
        : this(stream, profile, leaveOpen: false)
    { }

    internal CsvDataReader(Stream stream, CsvProfile profile, bool leaveOpen)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString), leaveOpen)
    {
        _rowNumberOptions = profile.RowNumber;
        if (_rowNumberOptions is not null)
            _rowNumberRecord = new RowNumberRecord();
    }

    protected override int GetSchemaOrdinal(int i) => HasRowNumber ? i - 1 : i;

    protected override FieldDescriptor GetFieldDescriptor(int i)
        => HasRowNumber && i == 0 ? RowNumberField : base.GetFieldDescriptor(i);

    protected override bool TryGetFieldDescriptor(int i,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FieldDescriptor? field)
    {
        if (HasRowNumber && i == 0)
        {
            field = RowNumberField;
            return true;
        }
        return base.TryGetFieldDescriptor(i, out field);
    }

    public override object GetValue(int i)
        => HasRowNumber && i == 0 ? CurrentRowNumber : base.GetValue(i);

    /// <inheritdoc />
    public override bool IsValid(int i)
    {
        if (!HasRowNumber || i != 0)
            return base.IsValid(i);
        EnsureValidationRecord();
        return true;
    }

    internal T MapCurrent<T>(SpanMapper<T> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        var record = (Record ?? throw new InvalidOperationException("No current record is available.")).AsCharacters();
        return mapper(record.Span.Span, record.FieldSpans);
    }

    protected override BaseRecordParser<CsvProfile> CreateRecordParser(StreamReader reader, CsvProfile profile)
        => new RecordParser(reader, profile);

    protected override IRecordSource<CsvProfile>? CreateStreamRecordSource(Stream stream, CsvProfile profile)
        => CsvStreamSource.Create(stream, profile, leaveOpen: true);

    protected override ValueTask<IRecordSource<CsvProfile>?> CreateStreamRecordSourceAsync(Stream stream, CsvProfile profile, CancellationToken cancellationToken)
        => CsvStreamSource.CreateAsync(stream, profile, cancellationToken);

    public override int FieldCount =>
        Record?.ParsedFields.Length ?? throw new InvalidOperationException("Fields are not defined yet.");

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
        => Record!.ParsedFields[i].Value.WasQuoted
            ? $"{Profile.Dialect.QuoteChar}{Record!.Slice(i)}{Profile.Dialect.QuoteChar}"
            : Record!.Slice(i).ToString();

    private protected override FieldEscaper CreateFieldEscaper() => new(Profile);

    private protected override bool TryGetDirectString(int i, out string value)
    {
        value = null!;
        // Derived readers may override GetValueOrThrow to transform strings.
        if (GetType() != typeof(CsvDataReader)) return false;
        // ASCII can decode directly into the returned string. Multibyte text
        // uses the pooled char cache to avoid GetString's separate UTF-8
        // character-count pass, and to reuse decoding for repeated access.
        var record = Record;
        if (record is null || i < 0 || i >= record.ParsedFields.Length || !record.TryGetUtf8(i, out var bytes)
            || Profile.ParserOptimizations.PoolString is not null || Profile.Schema is not null
            || Profile.Resource?.Sequences is { IsEmpty: false }
            || (Profile.ParserOptimizations.UnescapeChars && record.ParsedFields[i].Value.IsEscaped)
            || !Ascii.IsValid(bytes.Span))
            return false;
        value = Encoding.UTF8.GetString(bytes.Span);
        return true;
    }

    private protected override bool TryGetDirectValue<T>(int i, out T value)
    {
        value = default!;
        if (HasRowNumber && i == 0 && typeof(T) == typeof(int))
        {
            value = (T)(object)CurrentRowNumber;
            return true;
        }
        // Custom parsers, formats, and sanitizers retain their existing char path.
        if (Profile.Schema is not null || Profile.Parsers is not null
            || Profile.Resource?.Sequences is { IsEmpty: false }
            || Record is null || i < 0 || i >= Record.ParsedFields.Length
            || Record.ParsedFields[i].Value.IsEscaped || !Record.TryGetUtf8(i, out var memory))
            return false;
        var bytes = memory.Span;
        if (typeof(T) == typeof(int) && System.Buffers.Text.Utf8Parser.TryParse(bytes, out int integer, out int consumed) && consumed == bytes.Length)
            value = (T)(object)integer;
        else if (typeof(T) == typeof(long) && System.Buffers.Text.Utf8Parser.TryParse(bytes, out long longInteger, out consumed) && consumed == bytes.Length)
            value = (T)(object)longInteger;
        else if (typeof(T) == typeof(short) && System.Buffers.Text.Utf8Parser.TryParse(bytes, out short shortInteger, out consumed) && consumed == bytes.Length)
            value = (T)(object)shortInteger;
        else if (typeof(T) == typeof(bool) && System.Buffers.Text.Utf8Parser.TryParse(bytes, out bool boolean, out consumed) && consumed == bytes.Length)
            value = (T)(object)boolean;
        else
            return false;
        return true;
    }

    /// <summary>
    /// Returns the sanitized value of the field at the specified index, applying field-specific sanitization and handling missing or incomplete fields according to the CSV profile.
    /// </summary>
    /// <param name="i">The zero-based index of the field to retrieve.</param>
    /// <returns>A <see cref="NullableSpan"/> containing the sanitized field value, or a missing field value if the index is within the expected range and incomplete record extension is enabled.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the field index is outside the range of available and expected fields.</exception>
    protected override NullableSpan GetValueOrThrow(int i)
    {
        if (HasRowNumber && i == 0)
            return new NullableSpan(Record!.Slice(0).Span);
        if (i < Record!.ParsedFields.Length)
        {
            var sanitizer = GetSanitizer(i, Profile.ParserOptimizations);
            return sanitizer.Sanitize(Record!.Slice(i).Span, Record!.ParsedFields[i].Value.IsEscaped, Record!.ParsedFields[i].Value.WasQuoted);
        }
        if (i < Fields!.Length && Profile.ParserOptimizations.ExtendIncompleteRecords)
            return new NullableSpan(GetMissingField().ToString().AsSpan());
        throw new ArgumentOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.ParsedFields.Length} defined fields.");
    }

    /// <summary>
    /// Advances the reader to the next CSV record.
    /// </summary>
    /// <returns><c>true</c> if a new record was read; <c>false</c> if the end of the file has been reached.</returns>
    protected override bool ReadCore() => ReadRow();

    protected override ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
        => ReadRowAsync(cancellationToken);

    /// <summary>
    /// Reads the next non-comment CSV row, handling headers and end-of-file conditions as needed.
    /// </summary>
    /// <returns>True if a data row was successfully read; false if end-of-file is reached.</returns>
    protected virtual bool ReadRow()
    {
        var firstRow = RowCount == 0;
        if (firstRow && (Fields?.Length ?? 0) == 0 && Profile.Dialect.Header)
            RegisterHeader(CsvHeaders.Read(RecordSource!), "field_");

        RecordSpan rawRecord;
        RecordState state;
        do
        {
            if (IsEof)
                return false;

            IsEof = RecordSource!.IsEndOfFile(out rawRecord, out state);
            if (IsEof && (rawRecord.ParsedFields?.Length ?? 0) == 0)
            {
                Record = RecordMemory.Empty;
                return false;
            }
            RowCount++;
            if (Profile.Dialect.CommentRows?.Contains(RowCount) == true)
                PositionTracker.ExcludeRecord(RecordSource!);
        } while ((Profile.Dialect.CommentRows?.Contains(RowCount) ?? false) || state == RecordState.Comment);

        if (firstRow && (Fields?.Length ?? 0) == 0)
            RegisterHeader([(string?[])Array.CreateInstance(typeof(string), rawRecord.ParsedFields!.Length)], "field_");

        Record = rawRecord.AsMemory();
        CompleteRecord();
        return true;
    }

    protected virtual async ValueTask<bool> ReadRowAsync(CancellationToken cancellationToken)
    {
        var firstRow = RowCount == 0;
        if (firstRow && (Fields?.Length ?? 0) == 0 && Profile.Dialect.Header)
            RegisterHeader(await CsvHeaders.ReadAsync(RecordSource!, cancellationToken).ConfigureAwait(false), "field_");

        RecordReadResult result;
        do
        {
            if (IsEof)
                return false;

            result = await RecordSource!.ReadAsync(cancellationToken).ConfigureAwait(false);
            IsEof = result.IsEndOfFile;
            if (IsEof && (result.Record.ParsedFields?.Length ?? 0) == 0)
            {
                Record = RecordMemory.Empty;
                return false;
            }
            RowCount++;
            if (Profile.Dialect.CommentRows?.Contains(RowCount) == true)
                PositionTracker.ExcludeRecord(RecordSource!);
        } while ((Profile.Dialect.CommentRows?.Contains(RowCount) ?? false) || result.State == RecordState.Comment);

        if (firstRow && (Fields?.Length ?? 0) == 0)
            RegisterHeader([(string?[])Array.CreateInstance(typeof(string), result.Record.ParsedFields!.Length)], "field_");

        Record = result.Record;
        CompleteRecord();
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
        var fields = (Profile.Dialect.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{unamedPrefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{unamedPrefix}{unnamedFieldIndex++}")).ToArray();
        SetHeaders(fields);
    }

    internal void SetHeaders(string[] headers)
    {
        if (_rowNumberOptions is null)
        {
            Fields = headers;
            return;
        }
        if (headers.Contains(_rowNumberOptions.ColumnName, StringComparer.Ordinal))
            throw new InvalidDataException($"The generated row-number column '{_rowNumberOptions.ColumnName}' conflicts with a source column. Choose another name using WithRowNumber(columnName) or RowNumberOptions.");
        Fields = [_rowNumberOptions.ColumnName, .. headers];
    }

    internal string[]? SourceHeaders => HasRowNumber ? Fields?[1..] : Fields;

    private void CompleteRecord()
    {
        HandleUnexpectedFields(Fields!.Length - (HasRowNumber ? 1 : 0));
        if (_rowNumberRecord is not null)
        {
            CurrentRowNumber = checked(CurrentRowNumber + 1);
            Record = _rowNumberRecord.Prepend(Record!, CurrentRowNumber, Profile.Dialect.Delimiter);
        }
    }

    /// <summary>
    /// Throws an exception if the current record contains more fields than the expected count.
    /// </summary>
    /// <param name="expectedLength">The expected number of fields for the current record.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the record has more fields than expected, indicating the row number and the number of extra fields.
    /// </exception>
    private void HandleUnexpectedFields(int expectedLength)
    {
        var length = Record!.ParsedFields.Length;
        if (expectedLength < length)
        {
            var rowNumber = RowCount + (
                Profile.Dialect.Header
                ? Math.Max(Profile.Dialect.HeaderRows.Length, 1)
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
