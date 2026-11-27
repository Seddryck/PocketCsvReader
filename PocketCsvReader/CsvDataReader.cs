using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;
using PocketCsvReader.ValueReader;

namespace PocketCsvReader;
public partial class CsvDataReader : BaseDataReader<CsvProfile>, IUtf8DataRecord
{
    private readonly RowNumberOptions? _rowNumberOptions;
    private readonly RowNumberRecord? _rowNumberRecord;
    private readonly bool _hasNoSpecialSequences;
    private Utf8StringPool? _utf8StringPool;
    private static readonly FieldDescriptor RowNumberField = new(typeof(int), DataSourceTypeName: "integer");
    private static readonly FieldDescriptor UndeclaredField = new(typeof(string));
    internal bool HasRowNumber => _rowNumberOptions is not null;
    internal int CurrentRowNumber { get; set; }
    internal bool AllowUndeclaredFields { private get; set; }

    protected new RecordParser RecordParser => (RecordParser)base.RecordParser!;

    public CsvDataReader(Stream stream, CsvProfile profile)
        : this(stream, profile, leaveOpen: false)
    { }

    internal CsvDataReader(Stream stream, CsvProfile profile, bool leaveOpen)
        : base(stream, profile, CreateStringMapper(profile), leaveOpen)
    {
        (_rowNumberOptions, _rowNumberRecord) = CreateRowNumber(profile);
        _hasNoSpecialSequences = profile.Schema is null && profile.Resource?.Sequences is not { IsEmpty: false };
    }

    internal CsvDataReader(TextReader reader, CsvProfile profile, bool leaveOpen)
        : base(profile, CreateStringMapper(profile),
              () => new RecordParser(reader, profile), leaveOpen ? null : reader)
    {
        (_rowNumberOptions, _rowNumberRecord) = CreateRowNumber(profile);
        _hasNoSpecialSequences = profile.Schema is null && profile.Resource?.Sequences is not { IsEmpty: false };
    }

    private static StringMapper CreateStringMapper(CsvProfile profile)
        => new(profile.ParserOptimizations.PoolString, profile.ParserOptimizations.PoolStringLastValueHint,
            profile.ParserOptimizations.PoolDecodedStrings);

    private static (RowNumberOptions? Options, RowNumberRecord? Record) CreateRowNumber(CsvProfile profile)
    {
        var options = profile.RowNumber;
        return (options, options is null ? null : new RowNumberRecord());
    }

    protected override int GetSchemaOrdinal(int i) => GetFieldCacheOrdinal(i) - RowNumberWidth;

    protected override FieldDescriptor GetFieldDescriptor(int i)
    {
        if (AllowUndeclaredFields || HasComputedFields)
            return TryGetFieldDescriptor(i, out var field) ? field : UndeclaredField;
        return HasRowNumber && i == 0 ? RowNumberField : base.GetFieldDescriptor(i);
    }

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
    {
        return HasRowNumber && i == 0 ? CurrentRowNumber : base.GetValue(i);
    }

    internal Type GetDataTableColumnType(int i)
    {
        var type = TryGetFieldDescriptor(i, out var field) ? field.RuntimeType : typeof(string);
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <inheritdoc />
    public override bool IsValid(int i)
    {
        if (!HasRowNumber || i != 0)
            return base.IsValid(i);
        EnsureValidationRecord();
        return true;
    }

    internal T MapCurrent<T>(SpanMapper<T> mapper, bool defaultMapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        if (defaultMapper && (Profile.Filters.Count != 0 || _projectedOrdinals is not null))
            return FilteredObjectMapper<T>.Map(this);
        if (_projectedOrdinals is not null)
            return MapProjectedRecord(mapper);
        // The compatibility Record getter snapshots borrowed metadata. A public
        // mapper can therefore retain its IEnumerable<FieldSpan> after advancement.
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
        ((!_usingSourceFields && (_projectedOrdinals is not null || HasComputedFields)) ? Fields!.Length : SourceFieldCount)
        ?? throw new InvalidOperationException("Fields are not defined yet.");

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
    {
        if (GetComputedField(i) is not null)
            throw new NotSupportedException("Computed fields have no raw CSV text.");
        if (GetRecordField(i) is not { } field)
            throw new ArgumentOutOfRangeException(nameof(i));
        var text = GetCsvFieldMemory(i);
        return field.Value.WasQuoted ? $"{Profile.Dialect.QuoteChar}{text}{Profile.Dialect.QuoteChar}" : text.ToString();
    }

    private protected override FieldEscaper CreateFieldEscaper() => new(Profile);

    bool IUtf8DataRecord.CanGetUtf8Span(int ordinal)
        => RecordSource is Utf8RecordSource && CanBorrowUtf8Text(ordinal);

    /// <inheritdoc />
    public bool TryGetUtf8Span(int ordinal, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (RecordSource is not Utf8RecordSource source || !CanBorrowUtf8Text(ordinal)
            || HasBoundFieldParser(ordinal)
            || !TryGetCurrentUtf8(GetPhysicalOrdinal(ordinal), out var bytes, out var escaped))
            return false;

        var span = bytes.Span;
        if (!IsValidUtf8(span))
            return false;

        value = escaped && Profile.ParserOptimizations.UnescapeChars ? source.Unescape(span) : span;
        return true;
    }

    private bool CanBorrowUtf8Text(int ordinal)
        => IsSourceTextColumn(ordinal) && HasUntransformedText(ordinal);

    // Subclasses can override the logical value.
    private bool IsSourceTextColumn(int ordinal)
        => HasActiveRecord && GetType() == typeof(CsvDataReader) && (uint)ordinal < (uint)FieldCount
            && !(HasRowNumber && ordinal == 0) && GetComputedField(ordinal) is null;

    private bool HasUntransformedText(int ordinal)
    {
        var options = Profile.ParserOptimizations;
        if (options.HandleSpecialValues && Profile.Resource?.Sequences is { IsEmpty: false })
            return false;

        if (TryGetFieldDescriptor(ordinal, out var field))
        {
            return field.RuntimeType == typeof(string) && !HasCustomParser(field)
                && (!options.HandleSpecialValues || field.Sequences is not { IsEmpty: false })
                && Profile.Parsers?.HasParser(typeof(string)) is not true;
        }

        // Undeclared schema cells may require field-specific validation. A custom
        // string pool can replace the text of a field that has no declared parser.
        return Profile.Schema is null && options.PoolString is null;
    }

    private protected override bool TryGetDirectString(int i, out string value)
    {
        value = null!;
        // Derived readers may override GetValueOrThrow to transform strings.
        if (GetType() != typeof(CsvDataReader)) return false;
        // ASCII can decode directly into the returned string. Multibyte text
        // uses the pooled char cache to avoid GetString's separate UTF-8
        // character-count pass, and to reuse decoding for repeated access.
        if (Profile.ParserOptimizations.PoolString is not null || Profile.Schema is not null
            || Profile.Resource?.Sequences is { IsEmpty: false }
            || (HasRowNumber && i == 0) || !TryGetCsvUtf8(i, out var bytes, out var isEscaped)
            || (Profile.ParserOptimizations.UnescapeChars && isEscaped))
            return false;

        if (Profile.ParserOptimizations.PoolUtf8Strings)
        {
            value = (_utf8StringPool ??= new()).GetString(i, bytes.Span);
            return true;
        }

        if (!Ascii.IsValid(bytes.Span))
            return false;
        value = Encoding.UTF8.GetString(bytes.Span);
        return true;
    }

    private protected override bool TryGetDirectValue<T>(int i, out T value)
    {
        value = default!;
        if (HasRowNumber && i == 0)
        {
            if (typeof(T) != typeof(int))
                return false;

            value = (T)(object)CurrentRowNumber;
            return true;
        }

        var parser = ResolveUtf8Parser<T>(out var isConfigured);
        return parser is not null
            && TryGetDirectUtf8(i, out var memory)
            && CanParseUtf8Field<T>(i)
            && (!isConfigured || IsValidUtf8(memory.Span))
            && parser(memory.Span, out value);
    }

    private TryParseUtf8<T>? ResolveUtf8Parser<T>(out bool isConfigured)
    {
        var registrations = Profile.Parsers;
        var configured = registrations?.FindUtf8Parser<T>();
        isConfigured = configured is not null;
        return configured ?? (registrations?.HasParser(typeof(T)) is true ? null : DefaultUtf8Parsers.For<T>.Parser);
    }

    private bool TryGetDirectUtf8(int i, out ReadOnlyMemory<byte> memory)
    {
        memory = default;
        return GetType() == typeof(CsvDataReader)
            && Profile.Resource?.Sequences is not { IsEmpty: false }
            && TryGetCsvUtf8(i, out memory, out var isEscaped)
            && !isEscaped;
    }

    private bool CanParseUtf8Field<T>(int i)
    {
        if (Profile.Schema is null)
            return true;

        return TryGetFieldDescriptor(i, out var descriptor)
            && descriptor.RuntimeType == typeof(T)
            && !HasCustomParser(descriptor)
            && descriptor.Sequences is not { IsEmpty: false };
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> utf8)
    {
        if (Ascii.IsValid(utf8))
            return true;

        while (!utf8.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(utf8, out _, out var consumed) != OperationStatus.Done)
                return false;
            utf8 = utf8[consumed..];
        }
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
        if (GetComputedField(i) is not null)
            throw new NotSupportedException("Computed fields have no source text to parse.");
        if (HasRowNumber && i == 0)
            return GetCsvFieldMemory(0);
        // Plain fields can keep their backing memory without running the sanitizer.
        if (_hasNoSpecialSequences && !HasRowNumber && _projectedOrdinals is null
            && (uint)i < (uint)CurrentFieldSpanCount
            && !IsCurrentFieldEscaped(i))
            return GetCurrentFieldMemory(i);

        if (GetRecordField(i) is { } field)
        {
            var sanitizer = GetFieldSanitizer(i, Profile.ParserOptimizations);
            return sanitizer.Sanitize((NullableSpan)GetCsvFieldMemory(i), field.Value.IsEscaped, field.Value.WasQuoted);
        }
        if ((uint)i < (uint)Fields!.Length && Profile.ParserOptimizations.ExtendIncompleteRecords)
            return GetMissingField().ToString().AsMemory();
        throw new ArgumentOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {CurrentFieldSpanCount} defined fields.");
    }

    /// <summary>
    /// Advances the reader to the next CSV record.
    /// </summary>
    /// <returns><c>true</c> if a new record was read; <c>false</c> if the end of the file has been reached.</returns>
    protected override bool ReadCore() => _selectRecord is null ? ReadRow() : ReadSelectedRow();

    protected override ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
        => _selectRecord is null ? ReadRowAsync(cancellationToken) : ReadSelectedRowAsync(cancellationToken);

    /// <summary>
    /// Reads the next non-comment CSV row, handling headers and end-of-file conditions as needed.
    /// </summary>
    /// <returns>True if a data row was successfully read; false if end-of-file is reached.</returns>
    protected virtual bool ReadRow()
    {
        var firstRow = RowCount == 0;
        if (firstRow && (Fields?.Length ?? 0) == 0 && Profile.Dialect.Header)
            RegisterHeader(CsvHeaders.Read(RecordSource!));

        if (IsEof)
            return false;

        return IndexedRecordSource is { } indexed && Profile.Dialect.CommentRows is not { Length: > 0 }
            ? ReadIndexedRow(indexed, firstRow)
            : ReadOrdinaryRow(firstRow);
    }

    private bool ReadIndexedRow(Utf8RecordSource source, bool firstRow)
    {
        var fieldCount = source.AdvanceBuffered();
        if (fieldCount != 0)
            return PublishIndexedRow(source, firstRow, fieldCount);

        InvalidateDeferredRecord();
        var result = source.ReadForDataReader(out fieldCount);
        IsEof = result.IsEndOfFile;
        if (fieldCount != 0)
            return PublishIndexedRow(source, firstRow, fieldCount);
        if (IsEof && result.Record.FieldCount == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        RowCount++;
        return PublishOrdinaryRow(result.Record, firstRow);
    }

    private bool PublishIndexedRow(Utf8RecordSource source, bool firstRow, int fieldCount)
    {
        RowCount++;
        RegisterHeaderlessFields(firstRow, fieldCount);
        SetDeferredRecord(source, fieldCount);
        CompleteRecord(fieldCount);
        return true;
    }

    private bool ReadOrdinaryRow(bool firstRow)
    {
        InvalidateDeferredRecord();
        CurrentRecordResult result;
        do
        {
            if (IsEof)
                return false;

            result = ReadSource();
            IsEof = result.IsEndOfFile;
            if (IsEof && result.Record.FieldCount == 0)
            {
                Record = RecordMemory.Empty;
                return false;
            }
            RowCount++;
            if (Profile.Dialect.CommentRows?.Contains(RowCount) == true)
                PositionTracker.ExcludeRecord(RecordSource!);
        } while ((Profile.Dialect.CommentRows?.Contains(RowCount) ?? false) || result.State == RecordState.Comment);

        return PublishOrdinaryRow(result.Record, firstRow);
    }

    private bool PublishOrdinaryRow(CurrentRecord record, bool firstRow)
    {
        RegisterHeaderlessFields(firstRow, record.FieldCount);
        SetRecord(record, copyMetadataOnMaterialization: true);
        CompleteRecord(record.FieldCount);
        return true;
    }

    private void RegisterHeaderlessFields(bool firstRow, int fieldCount)
    {
        if (firstRow && (Fields?.Length ?? 0) == 0)
            RegisterHeader([new string?[fieldCount]]);
    }

    protected virtual async ValueTask<bool> ReadRowAsync(CancellationToken cancellationToken)
    {
        InvalidateDeferredRecord();
        var firstRow = RowCount == 0;
        if (firstRow && (Fields?.Length ?? 0) == 0 && Profile.Dialect.Header)
            RegisterHeader(await CsvHeaders.ReadAsync(RecordSource!, cancellationToken).ConfigureAwait(false));

        CurrentRecordResult result;
        do
        {
            if (IsEof)
                return false;

            result = await ReadSourceAsync(cancellationToken).ConfigureAwait(false);
            IsEof = result.IsEndOfFile;
            if (IsEof && result.Record.FieldCount == 0)
            {
                Record = RecordMemory.Empty;
                return false;
            }
            RowCount++;
            if (Profile.Dialect.CommentRows?.Contains(RowCount) == true)
                PositionTracker.ExcludeRecord(RecordSource!);
        } while ((Profile.Dialect.CommentRows?.Contains(RowCount) ?? false) || result.State == RecordState.Comment);

        if (firstRow && (Fields?.Length ?? 0) == 0)
            RegisterHeader([new string?[result.Record.FieldCount]]);

        SetRecord(result.Record, copyMetadataOnMaterialization: true);
        CompleteRecord(result.Record.FieldCount);
        return true;
    }

    private void RegisterHeader(string?[][] headers) => SetHeaders(CsvHeaders.GetFieldNames(headers, Profile.Dialect));

    internal void SetHeaders(string[] headers)
    {
        if (_rowNumberOptions is { } rowNumber && headers.Contains(rowNumber.ColumnName, StringComparer.Ordinal))
            throw new InvalidDataException($"The generated row-number column '{rowNumber.ColumnName}' conflicts with a source column. Choose another name using WithRowNumber(columnName) or RowNumberOptions.");

        _sourceHeaders = headers;
        var sourceFields = AddRowNumberHeader(headers);
        Fields = sourceFields;
        if (Profile.Projection is { } projection)
        {
            try
            {
                _projectedOrdinals = projection.Bind(headers);
            }
            catch (InvalidDataException)
            {
                IsEof = true;
                throw;
            }

            _sourceFields = sourceFields;
            Fields = _outputFields = AddRowNumberHeader(Array.ConvertAll(_projectedOrdinals, ordinal => headers[ordinal]));
        }

        try
        {
            BindComputedFields(sourceFields);
            if (HasComputedFields)
            {
                _sourceFields = sourceFields;
                _outputFields = Fields;
            }
        }
        catch (InvalidDataException)
        {
            IsEof = true;
            throw;
        }
    }

    internal string[]? SourceHeaders => _sourceHeaders;

    private string[] AddRowNumberHeader(string[] headers)
        => _rowNumberOptions is { } options ? [options.ColumnName, .. headers] : headers;

    private void CompleteRecord(int fieldCount)
    {
        var expectedFieldCount = _projectedOrdinals is not null || HasComputedFields
            ? _sourceHeaders!.Length
            : Fields!.Length - RowNumberWidth;
        HandleUnexpectedFields(fieldCount, expectedFieldCount);
        if (HasRowNumber)
        {
            CurrentRowNumber = checked(CurrentRowNumber + 1);
            _rowNumberText = null;
            if (_projectedOrdinals is null)
                Record = _rowNumberRecord!.Prepend(Record!, CurrentRowNumber, Profile.Dialect.Delimiter);
        }
    }

    /// <summary>
    /// Throws an exception if the current record contains more fields than the expected count.
    /// </summary>
    /// <param name="length">The number of fields in the current record.</param>
    /// <param name="expectedLength">The expected number of fields for the current record.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the record has more fields than expected, indicating the row number and the number of extra fields.
    /// </exception>
    private void HandleUnexpectedFields(int length, int expectedLength)
    {
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
