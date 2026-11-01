using PocketCsvReader.FieldParsing;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.SecurityLogs;

public sealed class SecurityLogDataReader : BaseDataReader<SecurityLogProfile>
{
    private SecurityLogRecordSource Source => (SecurityLogRecordSource)RecordSource!;
    private bool _current;
    private ISanitizer?[]? _sanitizers;

    public SecurityLogDataReader(Stream stream, SecurityLogProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    {
        if (profile.Attributes is not null)
            Fields = [.. SecurityLogParser.HeaderNames(profile.Format), .. profile.Attributes];
    }

    protected override IRecordSource<SecurityLogProfile> CreateRecordSource(StreamReader reader, SecurityLogProfile profile)
        => new SecurityLogRecordSource(reader, profile);

    public override int FieldCount => Fields?.Length ?? 0;
    protected override object GetNullValue(int i) => DBNull.Value;

    public override string GetRawString(int i)
    {
        ValidateOrdinal(i);
        var value = CurrentFieldSpans![i].Value;
        return CurrentRecordMemory.Span.Slice(value.Start, value.Length).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ValidateOrdinal(i);
        var field = CurrentFieldSpans![i];
        if (field.Value.IsNull) return default;
        var value = field.DecodedValue is { } decoded ? decoded.AsSpan()
            : CurrentRecordMemory.Span.Slice(field.Value.Start, field.Value.Length);
        _sanitizers ??= new ISanitizer?[FieldCount];
        if (_sanitizers[i] is not { } sanitizer)
        {
            TryGetFieldDescriptor(i, out var descriptor);
            sanitizer = new SanitizerFactory(Profile.ParserOptimizations).Create(
                SequenceCollection.Concat(Profile.Resource?.Sequences, descriptor?.Sequences));
            _sanitizers[i] = sanitizer;
        }
        return sanitizer.Sanitize(value, false, false);
    }

    protected override bool ReadCore()
    {
        _current = false;
        IsEof = Source.IsEndOfFile(out var record, out var state);
        Fields = Source.Fields ?? Fields;
        if (state == RecordState.Eof) { Record = RecordMemory.Empty; return false; }
        SetRecord(record.Memory, record.FieldSpans);
        RowCount++;
        return _current = true;
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        _current = false;
        var result = await Source.ReadAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
        Fields = Source.Fields ?? Fields;
        if (result.State == RecordState.Eof) { Record = RecordMemory.Empty; return false; }
        Record = result.Record;
        RowCount++;
        return _current = true;
    }

    private void ValidateOrdinal(int i)
    {
        if (!_current || IsClosed) throw new InvalidOperationException("No current record is available.");
        if (i < 0 || i >= FieldCount) throw new ArgumentOutOfRangeException(nameof(i));
    }
}
