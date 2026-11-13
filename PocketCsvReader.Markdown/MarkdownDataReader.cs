using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Markdown;

public sealed class MarkdownDataReader(Stream stream, MarkdownProfile profile)
    : BaseDataReader<MarkdownProfile>(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
{
    public override int FieldCount => Fields?.Length ?? 0;

    protected override IRecordSource<MarkdownProfile> CreateRecordSource(StreamReader reader, MarkdownProfile profile)
        => new MarkdownRecordSource(reader, profile);

    public override string GetRawString(int i)
    {
        _ = GetFields(i);
        return GetCurrentFieldMemory(i).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        var fields = GetFields(i);
        var memory = GetCurrentFieldMemory(i);
        ref var field = ref fields[i];
        var value = field.Value.IsEscaped
            ? (field.DecodedValue ??= MarkdownTableRow.DecodePipeEscapes(memory)).AsSpan()
            : memory.Span;
        return GetSanitizer(i, Profile.ParserOptimizations).Sanitize(value, false, false);
    }

    protected override bool ReadCore() => Accept(ReadSource());

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
        => Accept(await ReadSourceAsync(cancellationToken).ConfigureAwait(false));

    private bool Accept(CurrentRecordResult result)
    {
        Fields = (RecordSource as MarkdownRecordSource)?.Header;
        IsEof = result.IsEndOfFile;
        SetRecord(result.Record);
        if (result.State != RecordState.Record) return false;
        RowCount++;
        return true;
    }

    private FieldSpan[] GetFields(int i)
    {
        if (CurrentFieldSpans is not { } fields)
            throw new InvalidOperationException("Read must be called before accessing field values.");
        if ((uint)i >= (uint)fields.Length)
            throw new ArgumentOutOfRangeException(nameof(i));
        return fields;
    }
}
