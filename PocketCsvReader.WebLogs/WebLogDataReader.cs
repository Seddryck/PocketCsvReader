using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.WebLogs;

public sealed class WebLogDataReader : BaseDataReader<WebLogProfile>
{
    private WebLogRecordSource Source => (WebLogRecordSource)RecordSource!;

    public IReadOnlyList<W3cDirective> Directives => RecordSource is WebLogRecordSource source
        ? source.Directives
        : Array.Empty<W3cDirective>();

    public WebLogDataReader(Stream stream, WebLogProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    {
        if (profile.Format == WebLogFormat.Common)
            Fields = [.. WebLogRecordSource.CommonFields];
        else if (profile.Format == WebLogFormat.Combined)
            Fields = [.. WebLogRecordSource.CombinedFields];
    }

    protected override IRecordSource<WebLogProfile> CreateRecordSource(StreamReader reader, WebLogProfile profile)
        => new WebLogRecordSource(reader, profile);

    public override int FieldCount => Fields?.Length ?? 0;

    public override string GetRawString(int i)
    {
        ValidateOrdinal(i);
        var field = CurrentFieldSpans![i].Value;
        return CurrentRecordMemory.Span.Slice(field.Start, field.Length).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ValidateOrdinal(i);
        var field = CurrentFieldSpans![i].Value;
        return GetSanitizer(i, Profile.ParserOptimizations)
            .Sanitize(CurrentRecordMemory.Span.Slice(field.Start, field.Length), field.IsEscaped, field.WasQuoted);
    }

    protected override bool ReadCore()
    {
        IsEof = Source.IsEndOfFile(out var record, out _);
        Fields = Source.Fields;
        if (record.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        SetRecord(record.Memory, record.FieldSpans);
        RowCount++;
        return true;
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var result = await ReadSourceAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
        Fields = Source.Fields;
        if (result.State != RecordState.Record)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        SetRecord(result.Record);
        RowCount++;
        return true;
    }

    private void ValidateOrdinal(int i)
    {
        if (CurrentFieldSpans is null)
            throw new InvalidOperationException("Read must be called before accessing field values.");
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), $"Field index '{i}' is out of range.");
    }
}
