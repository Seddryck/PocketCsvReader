namespace PocketCsvReader;

// Internal current-record transport. Character/byte memory, metadata, and decoding
// storage are borrowed until the source advances; no heap wrapper is needed.
internal readonly struct CurrentRecord
{
    public ReadOnlyMemory<char> Characters { get; }
    private readonly ReadOnlyMemory<byte> _utf8;
    private readonly Array? _metadata;
    private readonly int _offsetIndex;
    private readonly int _compactCount;
    private readonly Utf8FieldBuffer? _decodedFields;
    public bool IsUtf8 => _decodedFields is not null;
    public bool HasFields => _metadata is not null;
    public int FieldCount => _compactCount != 0 ? _compactCount : ((FieldSpan[]?)_metadata)?.Length ?? 0;

    public CurrentRecord(ReadOnlyMemory<char> characters, FieldSpan[] fields)
        => (Characters, _metadata) = (characters, fields);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, FieldSpan[] fields, Utf8FieldBuffer decodedFields)
        => (_utf8, _metadata, _decodedFields) = (utf8, fields, decodedFields);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, int[] ends, int offsetIndex, int count, Utf8FieldBuffer decodedFields)
        => (_utf8, _metadata, _offsetIndex, _compactCount, _decodedFields) = (utf8, ends, offsetIndex, count, decodedFields);

    public bool TryGetUtf8(int ordinal, out ReadOnlyMemory<byte> value, out bool isEscaped)
    {
        isEscaped = false;
        if (_compactCount != 0)
        {
            if ((uint)ordinal < (uint)_compactCount)
            {
                GetCompactRange(ordinal, out int start, out int length);
                value = _utf8.Slice(start, length);
                return true;
            }
        }
        else if (IsUtf8 && _metadata is FieldSpan[] fields && (uint)ordinal < (uint)fields.Length)
        {
            var field = fields[ordinal].Value;
            value = _utf8.Slice(field.Start, field.Length);
            isEscaped = field.IsEscaped;
            return true;
        }

        value = default;
        return false;
    }

    private void GetCompactRange(int ordinal, out int start, out int length)
    {
        var ends = (int[])_metadata!;
        var index = _offsetIndex + ordinal;
        start = ordinal == 0 ? 0 : ends[index - 1] + 1;
        length = ends[index] - start;
    }

    private SpanInfo GetCompactValue(int ordinal)
    {
        GetCompactRange(ordinal, out int start, out int length);
        return new(length == 0 ? 0 : start, length, IsStarted: length != 0, IsComplete: true);
    }

    public bool IsEscaped(int ordinal) => _compactCount == 0 && ((FieldSpan[])_metadata!)[ordinal].Value.IsEscaped;

    public FieldSpan GetField(int ordinal) => _compactCount != 0
        ? new(GetCompactValue(ordinal), default)
        : ((FieldSpan[])_metadata!)[ordinal];

    public ReadOnlyMemory<char> Slice(int ordinal)
    {
        if ((uint)ordinal >= (uint)FieldCount)
            throw new IndexOutOfRangeException();

        var field = _compactCount != 0 ? GetCompactValue(ordinal) : ((FieldSpan[])_metadata!)[ordinal].Value;
        return IsUtf8
            ? _decodedFields!.Decode(_utf8.Span, field, ordinal)
            : Characters.Slice(field.Start, field.Length);
    }

    public FieldSpan[]? Fields
    {
        get
        {
            if (_compactCount == 0)
                return (FieldSpan[]?)_metadata;

            var fields = new FieldSpan[_compactCount];
            WriteFields(fields);
            return fields;
        }
    }

    public FieldSpan[]? BorrowFields()
    {
        if (_compactCount == 0)
            return (FieldSpan[]?)_metadata;

        var fields = _decodedFields!.PublishedFields.GetBorrowedStorage(_compactCount);
        WriteFields(fields);
        return fields;
    }

    private void WriteFields(FieldSpan[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
            fields[i] = new(GetCompactValue(i), default);
    }

    public CurrentRecord WithExpandedFields(FieldSpan[] fields)
        => _compactCount == 0 ? this : new(_utf8, fields, _decodedFields!);

    // This allocation belongs only to callers requiring the public wrapper API.
    public RecordMemory Materialize(bool copyMetadata = false)
    {
        if (Fields is not { } fields) return RecordMemory.Empty;
        if (copyMetadata && _compactCount == 0) fields = FieldMetadataBuffer.CopyOwned(fields);
        return IsUtf8 ? new RecordMemory(_utf8, fields, _decodedFields!) : new RecordMemory(Characters, fields);
    }
}

internal readonly record struct CurrentRecordResult(bool IsEndOfFile, CurrentRecord Record, RecordState State)
{
    public RecordReadResult Materialize() => new(IsEndOfFile, Record.Materialize(), State);
}

internal interface ICurrentRecordSource
{
    // Lifetime-stable capability, sampled once at reader initialization.
    bool SupportsCurrentRecord { get; }
    CurrentRecordResult ReadCurrent();
    ValueTask<CurrentRecordResult> ReadCurrentAsync(CancellationToken cancellationToken);
}

// Sources with borrowed sync and async paths materialize the compatibility
// wrapper only when called through the public record-source interface.
internal interface IBorrowedRecordSource<out TProfile> : IRecordSource<TProfile>, ICurrentRecordSource
{
    bool ICurrentRecordSource.SupportsCurrentRecord => true;

    CurrentRecordResult ICurrentRecordSource.ReadCurrent()
    {
        var eof = IsEndOfFile(out var record, out var state);
        return new(eof, record.Current, state);
    }

    async ValueTask<RecordReadResult> IRecordSource<TProfile>.ReadAsync(CancellationToken cancellationToken)
        => (await ReadCurrentAsync(cancellationToken).ConfigureAwait(false)).Materialize();
}
