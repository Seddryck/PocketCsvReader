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
        ValidateOrdinal(i);
        return GetCurrentFieldMemory(i).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ValidateOrdinal(i);
        var memory = GetCurrentFieldMemory(i);
        ref var field = ref CurrentFieldSpans![i];
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
        Fields = ((MarkdownRecordSource)RecordSource!).Header;
        IsEof = result.IsEndOfFile;
        SetRecord(result.Record);
        if (result.State != RecordState.Record) return false;
        RowCount++;
        return true;
    }

    private void ValidateOrdinal(int i)
    {
        if (CurrentFieldSpans is null)
            throw new InvalidOperationException("Read must be called before accessing field values.");
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)i, (uint)FieldCount, nameof(i));
    }
}
