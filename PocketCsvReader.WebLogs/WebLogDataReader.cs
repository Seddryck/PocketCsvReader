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
    }

    protected override IRecordSource<WebLogProfile> CreateRecordSource(StreamReader reader, WebLogProfile profile)
        => new WebLogRecordSource(reader, profile);

    public override int FieldCount => Fields?.Length ?? 0;

    public override string GetRawString(int i)
    {
        ValidateOrdinal(i);
        return Record!.Slice(i).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ValidateOrdinal(i);
        var field = Record!.FieldSpans[i];
        return GetSanitizer(i, Profile.ParserOptimizations)
            .Sanitize(Record.Slice(i).Span, field.Value.IsEscaped, field.Value.WasQuoted);
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

        Record = record.AsMemory();
        RowCount++;
        return true;
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var result = await Source.ReadAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
        Fields = Source.Fields;
        if (result.Record.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        Record = result.Record;
        RowCount++;
        return true;
    }

    private void ValidateOrdinal(int i)
    {
        if (Record is null)
            throw new InvalidOperationException("Read must be called before accessing field values.");
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), $"Field index '{i}' is out of range.");
    }
}
