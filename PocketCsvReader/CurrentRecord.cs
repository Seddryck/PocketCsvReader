namespace PocketCsvReader;

// Internal current-record transport. Character/byte memory, metadata, and decoding
// storage are borrowed until the source advances; no heap wrapper is needed.
internal readonly struct CurrentRecord
{
    public ReadOnlyMemory<char> Characters { get; }
    private readonly ReadOnlyMemory<byte> _utf8;
    private readonly Array? _metadata;
    private readonly int _offsetIndex;
    // A negative count denotes an expanded metadata slice. Zero uses the entire field array.
    private readonly int _metadataCount;
    // Compact character rows carry publication storage here. UTF-8 rows carry the decoder.
    private readonly object? _storage;
    public bool IsUtf8 => _storage is Utf8FieldBuffer;
    public bool HasFields => _metadata is not null;
    private bool IsCompact => _metadataCount > 0;
    public int FieldCount => IsCompact ? _metadataCount
        : _metadataCount < 0 ? -_metadataCount : ((FieldSpan[]?)_metadata)?.Length ?? 0;
    private ReadOnlySpan<FieldSpan> ExpandedFields => ((FieldSpan[]?)_metadata).AsSpan(_offsetIndex, FieldCount);

    public CurrentRecord(ReadOnlyMemory<char> characters, FieldSpan[] fields)
        => (Characters, _metadata) = (characters, fields);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, FieldSpan[] fields, Utf8FieldBuffer decodedFields)
        => (_utf8, _metadata, _storage) = (utf8, fields, decodedFields);

    internal CurrentRecord(ReadOnlyMemory<char> characters, FieldSpan[] fields, int offset, int count,
        FieldMetadataBuffer publishedFields)
        => (Characters, _metadata, _offsetIndex, _metadataCount, _storage) = (characters, fields, offset, -count, publishedFields);

    internal CurrentRecord(ReadOnlyMemory<byte> utf8, FieldSpan[] fields, int offset, int count, Utf8FieldBuffer decoder)
        => (_utf8, _metadata, _offsetIndex, _metadataCount, _storage) = (utf8, fields, offset, -count, decoder);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, int[] ends, int offsetIndex, int count, Utf8FieldBuffer decodedFields)
        => (_utf8, _metadata, _offsetIndex, _metadataCount, _storage) = (utf8, ends, offsetIndex, count, decodedFields);

    public CurrentRecord(ReadOnlyMemory<char> characters, CsvRowOffsets row, FieldMetadataBuffer publishedFields)
        => (Characters, _metadata, _offsetIndex, _metadataCount, _storage) =
            (characters, row.Ends, row.FirstField, row.FieldCount, publishedFields);

    public bool TryGetUtf8(int ordinal, out ReadOnlyMemory<byte> value, out bool isEscaped)
    {
        isEscaped = false;
        if (IsUtf8 && (uint)ordinal < (uint)FieldCount)
        {
            if (IsCompact)
            {
                var flags = GetCompactRange(ordinal, out int start, out int length);
                isEscaped = (flags & CsvFieldOffset.Escaped) != 0;
                value = _utf8.Slice(start, length);
            }
            else
            {
                var field = ((FieldSpan[])_metadata!)[_offsetIndex + ordinal].Value;
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

    public bool IsEscaped(int ordinal) => IsCompact
        ? (((int[])_metadata!)[_offsetIndex + ordinal] & CsvFieldOffset.Escaped) != 0
        : ((FieldSpan[])_metadata!)[_offsetIndex + ordinal].Value.IsEscaped;

    public FieldSpan GetField(int ordinal) => IsCompact
        ? new(GetCompactValue(ordinal), default)
        : ((FieldSpan[])_metadata!)[_offsetIndex + ordinal];

    public ReadOnlyMemory<char> Slice(int ordinal)
    {
        if ((uint)ordinal >= (uint)FieldCount)
            throw new IndexOutOfRangeException();

        if (IsCompact && _storage is FieldMetadataBuffer)
        {
            GetCompactRange(ordinal, out int start, out int length);
            return Characters.Slice(start, length);
        }

        var field = IsCompact ? GetCompactValue(ordinal) : ((FieldSpan[])_metadata!)[_offsetIndex + ordinal].Value;
        return _storage is Utf8FieldBuffer decoded
            ? decoded.Decode(_utf8.Span, field, ordinal)
            : Characters.Slice(field.Start, field.Length);
    }

    public FieldSpan[]? Fields
    {
        get
        {
            if (!IsCompact)
                return _metadataCount == 0 ? (FieldSpan[]?)_metadata : FieldMetadataBuffer.CopyOwned(ExpandedFields);

            var fields = new FieldSpan[_metadataCount];
            WriteFields(fields);
            return fields;
        }
    }

    public FieldSpan[]? BorrowFields()
    {
        if (_metadataCount == 0)
            return (FieldSpan[]?)_metadata;

        var storage = _storage is Utf8FieldBuffer decoded ? decoded.PublishedFields : (FieldMetadataBuffer)_storage!;
        if (!IsCompact)
            return storage.PublishBorrowed(ExpandedFields);

        var fields = storage.GetBorrowedStorage(_metadataCount);
        WriteFields(fields);
        return fields;
    }

    private void WriteFields(FieldSpan[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
            fields[i] = new(GetCompactValue(i), default);
    }

    internal CsvBatchRecord CopyForBatch(CsvBatchStorage storage, int[]? selectedOrdinals)
    {
        ReadOnlySpan<int> present = selectedOrdinals;
        int fieldCount = FieldCount;
        while (!present.IsEmpty && present[^1] >= fieldCount)
            present = present[..^1];

        return IsUtf8
            ? CopyBatchFields(_utf8.Span, ref storage.Bytes, storage, present, selectedOrdinals is not null, utf8: true)
            : CopyBatchFields(Characters.Span, ref storage.Characters, storage, present, selectedOrdinals is not null, utf8: false);
    }

    private CsvBatchRecord CopyBatchFields<T>(ReadOnlySpan<T> input, ref CsvBatchBuffer<T> data,
        CsvBatchStorage storage, ReadOnlySpan<int> selected, bool project, bool utf8)
    {
        if (!project)
        {
            int fieldStart = IsCompact
                ? storage.Offsets.Append(((int[])_metadata!).AsSpan(_offsetIndex, FieldCount))
                : storage.AppendFields(ExpandedFields);
            return new(data.Append(input), input.Length, fieldStart, FieldCount, utf8, IsCompact);
        }

        if (selected.IsEmpty)
            return new(data.Count, 0, 0, 0, utf8, Compact: false);

        return IsCompact
            ? CopyCompactSelection(input, ref data, storage, selected, utf8)
            : CopyExpandedSelection(input, ref data, storage, selected, utf8);
    }

    private CsvBatchRecord CopyExpandedSelection<T>(ReadOnlySpan<T> input, ref CsvBatchBuffer<T> data,
        CsvBatchStorage storage, ReadOnlySpan<int> selected, bool utf8)
    {
        var sourceFields = ExpandedFields;
        int fieldStart = storage.Fields.Reserve(selected.Length);
        var fields = storage.Fields.Slice(fieldStart, selected.Length);
        int length = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            var field = sourceFields[selected[i]];
            fields[i] = field with { Value = field.Value with { Start = length } };
            length += field.Value.Length;
        }

        int dataStart = data.Reserve(length);
        var target = data.Slice(dataStart, length);
        for (int i = 0; i < fields.Length; i++)
        {
            var value = sourceFields[selected[i]].Value;
            input.Slice(value.Start, value.Length).CopyTo(target[fields[i].Value.Start..]);
        }

        return new(dataStart, length, fieldStart, selected.Length, utf8, Compact: false);
    }

    private CsvBatchRecord CopyCompactSelection<T>(ReadOnlySpan<T> input, ref CsvBatchBuffer<T> data,
        CsvBatchStorage storage, ReadOnlySpan<int> selected, bool utf8)
    {
        var offsets = ((int[])_metadata!).AsSpan(_offsetIndex, FieldCount);
        if (selected[^1] == selected.Length - 1)
        {
            int prefixLength = offsets[selected[^1]] & CsvFieldOffset.OffsetMask;
            return new(data.Append(input[..prefixLength]), prefixLength,
                storage.Offsets.Append(offsets[..selected.Length]), selected.Length, utf8, Compact: true);
        }

        int fieldStart = storage.Offsets.Reserve(selected.Length);
        var selectedOffsets = storage.Offsets.Slice(fieldStart, selected.Length);
        int length = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            int ordinal = selected[i];
            int start = ordinal == 0 ? 0 : (offsets[ordinal - 1] & CsvFieldOffset.OffsetMask) + 1;
            int end = offsets[ordinal] & CsvFieldOffset.OffsetMask;
            length += end - start;
            selectedOffsets[i] = length | (offsets[ordinal] & ~CsvFieldOffset.OffsetMask);
            // Compact offsets expect a delimiter between adjacent retained fields.
            if (i + 1 < selected.Length)
                length++;
        }

        int dataStart = data.Reserve(length);
        var target = data.Slice(dataStart, length);
        for (int i = 0; i < selected.Length; i++)
        {
            int ordinal = selected[i];
            int start = ordinal == 0 ? 0 : (offsets[ordinal - 1] & CsvFieldOffset.OffsetMask) + 1;
            int end = offsets[ordinal] & CsvFieldOffset.OffsetMask;
            int destination = i == 0 ? 0 : (selectedOffsets[i - 1] & CsvFieldOffset.OffsetMask) + 1;
            input.Slice(start, end - start + (i + 1 < selected.Length ? 1 : 0)).CopyTo(target[destination..]);
        }

        return new(dataStart, length, fieldStart, selected.Length, utf8, Compact: true);
    }

    public CurrentRecord WithExpandedFields(FieldSpan[] fields)
        => _metadataCount == 0 ? this
            : _storage is Utf8FieldBuffer decoded ? new(_utf8, fields, decoded) : new(Characters, fields);

    // This allocation belongs only to callers requiring the public wrapper API.
    public RecordMemory Materialize(bool copyMetadata = false)
    {
        if (Fields is not { } fields) return RecordMemory.Empty;
        if (copyMetadata && _metadataCount == 0) fields = FieldMetadataBuffer.CopyOwnedTree(fields);
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
