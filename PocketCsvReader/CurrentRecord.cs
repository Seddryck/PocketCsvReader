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
    // Compact character rows carry publication storage here. UTF-8 rows carry the decoder.
    private readonly object? _storage;
    public bool IsUtf8 => _storage is Utf8FieldBuffer;
    public bool HasFields => _metadata is not null;
    public int FieldCount => _compactCount != 0 ? _compactCount : ((FieldSpan[]?)_metadata)?.Length ?? 0;

    public CurrentRecord(ReadOnlyMemory<char> characters, FieldSpan[] fields)
        => (Characters, _metadata) = (characters, fields);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, FieldSpan[] fields, Utf8FieldBuffer decodedFields)
        => (_utf8, _metadata, _storage) = (utf8, fields, decodedFields);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, int[] ends, int offsetIndex, int count, Utf8FieldBuffer decodedFields)
        => (_utf8, _metadata, _offsetIndex, _compactCount, _storage) = (utf8, ends, offsetIndex, count, decodedFields);

    public CurrentRecord(ReadOnlyMemory<char> characters, CsvRowOffsets row, FieldMetadataBuffer publishedFields)
        => (Characters, _metadata, _offsetIndex, _compactCount, _storage) =
            (characters, row.Ends, row.FirstField, row.FieldCount, publishedFields);

    public bool TryGetUtf8(int ordinal, out ReadOnlyMemory<byte> value, out bool isEscaped)
    {
        isEscaped = false;
        if (IsUtf8 && (uint)ordinal < (uint)FieldCount)
        {
            if (_compactCount != 0)
            {
                var flags = GetCompactRange(ordinal, out int start, out int length);
                isEscaped = (flags & CsvFieldOffset.Escaped) != 0;
                value = _utf8.Slice(start, length);
            }
            else
            {
                var field = ((FieldSpan[])_metadata!)[ordinal].Value;
                value = _utf8.Slice(field.Start, field.Length);
                isEscaped = field.IsEscaped;
            }

            return true;
        }

        value = default;
        return false;
    }

    private int GetCompactRange(int ordinal, out int start, out int length)
    {
        var ends = (int[])_metadata!;
        var index = _offsetIndex + ordinal;
        var end = ends[index];
        var trim = (end & CsvFieldOffset.Quoted) >> 30;
        start = (ordinal == 0 ? 0 : (ends[index - 1] & CsvFieldOffset.OffsetMask) + 1) + trim;
        length = (end & CsvFieldOffset.OffsetMask) - start - trim;
        return end;
    }

    private SpanInfo GetCompactValue(int ordinal)
    {
        var flags = GetCompactRange(ordinal, out int start, out int length);
        var wasQuoted = (flags & CsvFieldOffset.Quoted) != 0;
        var isStarted = wasQuoted || length != 0;
        return new(isStarted ? start : 0, length, wasQuoted,
            IsEscaped: (flags & CsvFieldOffset.Escaped) != 0, IsStarted: isStarted, IsComplete: true);
    }

    public bool IsEscaped(int ordinal) => _compactCount != 0
        ? (((int[])_metadata!)[_offsetIndex + ordinal] & CsvFieldOffset.Escaped) != 0
        : ((FieldSpan[])_metadata!)[ordinal].Value.IsEscaped;

    public FieldSpan GetField(int ordinal) => _compactCount != 0
        ? new(GetCompactValue(ordinal), default)
        : ((FieldSpan[])_metadata!)[ordinal];

    public ReadOnlyMemory<char> Slice(int ordinal)
    {
        if ((uint)ordinal >= (uint)FieldCount)
            throw new IndexOutOfRangeException();

        if (_storage is FieldMetadataBuffer)
        {
            GetCompactRange(ordinal, out int start, out int length);
            return Characters.Slice(start, length);
        }

        var field = _compactCount != 0 ? GetCompactValue(ordinal) : ((FieldSpan[])_metadata!)[ordinal].Value;
        return _storage is Utf8FieldBuffer decoded
            ? decoded.Decode(_utf8.Span, field, ordinal)
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

        var storage = _storage is Utf8FieldBuffer decoded ? decoded.PublishedFields : (FieldMetadataBuffer)_storage!;
        var fields = storage.GetBorrowedStorage(_compactCount);
        WriteFields(fields);
        return fields;
    }

    private void WriteFields(FieldSpan[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
            fields[i] = new(GetCompactValue(i), default);
    }

    public CurrentRecord WithExpandedFields(FieldSpan[] fields)
        => _compactCount == 0 ? this
            : _storage is Utf8FieldBuffer decoded ? new(_utf8, fields, decoded) : new(Characters, fields);

    // This allocation belongs only to callers requiring the public wrapper API.
    public RecordMemory Materialize(bool copyMetadata = false)
    {
        if (Fields is not { } fields) return RecordMemory.Empty;
        if (copyMetadata && _compactCount == 0) fields = FieldMetadataBuffer.CopyOwnedTree(fields);
        return _storage is Utf8FieldBuffer decoded ? new RecordMemory(_utf8, fields, decoded) : new RecordMemory(Characters, fields);
    }
}

internal readonly record struct CurrentRecordResult(bool IsEndOfFile, CurrentRecord Record, RecordState State)
{
    public RecordReadResult Materialize() => new(IsEndOfFile, Record.Materialize(), State);

    public RecordReadResult MaterializeOwned() => new(IsEndOfFile, Record.Materialize(copyMetadata: true), State);
}

// Character-only synchronous transport. The source owns both views until it advances.
internal interface ICharacterRecordSource
{
    bool ReadCharacters(out ReadOnlyMemory<char> characters, out FieldSpan[] fields, out RecordState state);

    static bool ReadOwned(ICharacterRecordSource source, out RecordSpan record, out RecordState state)
    {
        var eof = source.ReadCharacters(out var characters, out var fields, out state);
        record = RecordSpan.FromMemory(characters, FieldMetadataBuffer.CopyOwnedTree(fields));
        return eof;
    }
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

    bool CopyPublicMetadata => false;

    async ValueTask<RecordReadResult> IRecordSource<TProfile>.ReadAsync(CancellationToken cancellationToken)
    {
        var result = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        return CopyPublicMetadata ? result.MaterializeOwned() : result.Materialize();
    }
}

internal interface IBorrowedCharacterRecordSource<out TProfile> : IBorrowedRecordSource<TProfile>, ICharacterRecordSource
{
    bool IBorrowedRecordSource<TProfile>.CopyPublicMetadata => true;

    CurrentRecordResult ICurrentRecordSource.ReadCurrent()
    {
        var eof = ReadCharacters(out var characters, out var fields, out var state);
        return new(eof, state == RecordState.Eof ? default : new CurrentRecord(characters, fields), state);
    }
}
