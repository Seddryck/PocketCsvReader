using PocketCsvReader;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth;

public sealed class FixedWidthDataReader : BaseDataReader<FixedWidthProfile>
{
    private bool _headerProcessed;

    private FixedWidthRecordParser FixedWidthRecordSource
        => (FixedWidthRecordParser)RecordSource!;

    public FixedWidthDataReader(Stream stream, FixedWidthProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    {
        Fields = profile.Descriptor.Fields.Select(field => field.Name).ToArray();
    }

    protected override IRecordSource<FixedWidthProfile> CreateRecordSource(StreamReader reader, FixedWidthProfile profile)
        => new FixedWidthRecordParser(reader, profile);

    public override int FieldCount => Profile.Descriptor.Fields.Count;

    public override string GetRawString(int i)
    {
        ValidateOrdinal(i);
        return GetCurrentFieldMemory(i).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ValidateOrdinal(i);
        var span = Profile.Descriptor.Fields[i].TrimPadding(GetCurrentFieldMemory(i).Span);

        var sanitizer = GetSanitizer(i, Profile.ParserOptimizations);
        return sanitizer.Sanitize(span, false, false);
    }

    protected override bool ReadCore()
    {
        var source = FixedWidthRecordSource;
        if (!_headerProcessed)
        {
            _headerProcessed = true;
            if (Profile.Descriptor.Header)
            {
                IsEof = source.Header is null && source.ReadHeader();
                if (source.Header is { } header) Fields = header;
                if (IsEof) return false;
            }
        }

        IsEof = source.IsEndOfFile(out var rawRecord, out _);
        if (rawRecord.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        SetRecord(rawRecord.Memory, rawRecord.FieldSpans);
        RowCount++;
        return true;
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var source = FixedWidthRecordSource;
        if (!_headerProcessed)
        {
            _headerProcessed = true;
            if (Profile.Descriptor.Header)
            {
                IsEof = source.Header is null && await source.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
                if (source.Header is { } header) Fields = header;
                if (IsEof) return false;
            }
        }

        var result = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        IsEof = result.IsEndOfFile;
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
        if (CurrentFieldSpans is null)
            throw new InvalidOperationException("Read must be called before accessing field values.");
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), $"Field index '{i}' is out of range.");
    }
}
