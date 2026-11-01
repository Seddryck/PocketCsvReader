namespace PocketCsvReader;

// Internal current-record transport. Character/byte memory, metadata, and decoding
// storage are borrowed until the source advances; no heap wrapper is needed.
internal readonly struct CurrentRecord
{
    public ReadOnlyMemory<char> Characters { get; }
    private readonly ReadOnlyMemory<byte> _utf8;
    public FieldSpan[]? Fields { get; }
    private readonly Utf8FieldBuffer? _decodedFields;
    public bool IsUtf8 => _decodedFields is not null;

    public CurrentRecord(ReadOnlyMemory<char> characters, FieldSpan[] fields)
        => (Characters, Fields) = (characters, fields);

    public CurrentRecord(ReadOnlyMemory<byte> utf8, FieldSpan[] fields, Utf8FieldBuffer decodedFields)
        => (_utf8, Fields, _decodedFields) = (utf8, fields, decodedFields);

    public bool TryGetUtf8(int ordinal, out ReadOnlyMemory<byte> value)
    {
        if (!IsUtf8) { value = default; return false; }
        var field = Fields![ordinal].Value;
        value = _utf8.Slice(field.Start, field.Length);
        return true;
    }

    public ReadOnlyMemory<char> Slice(int ordinal)
    {
        var field = Fields![ordinal].Value;
        return IsUtf8
            ? _decodedFields!.Decode(_utf8.Span, field, ordinal)
            : Characters.Slice(field.Start, field.Length);
    }

    // This allocation belongs only to callers requiring the public wrapper API.
    public RecordMemory Materialize(bool copyMetadata = false)
    {
        if (Fields is null) return RecordMemory.Empty;
        var fields = copyMetadata ? FieldMetadataBuffer.CopyOwned(Fields) : Fields;
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
