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
        return Record!.Slice(i).ToString();
    }

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ValidateOrdinal(i);
        var span = Record!.Slice(i).Span;
        var layout = Profile.Descriptor.Fields[i];
        span = TrimPadding(span, layout.Padding, layout.PaddingChar);

        var sanitizer = GetSanitizer(i, Profile.ParserOptimizations);
        return sanitizer.Sanitize(span, false, false);
    }

    protected override bool ReadCore()
    {
        if (!_headerProcessed)
        {
            _headerProcessed = true;
            if (Profile.Descriptor.Header)
            {
                var headerEof = FixedWidthRecordSource.IsEndOfFile(out var header, out _);
                if (header.FieldSpans.Length == 0)
                {
                    IsEof = headerEof;
                    return false;
                }

                var headers = new string[Profile.Descriptor.Fields.Count];
                for (var i = 0; i < headers.Length; i++)
                {
                    var field = Profile.Descriptor.Fields[i];
                    headers[i] = TrimPadding(header.Slice(i), field.Padding, field.PaddingChar).ToString();
                }
                Fields = headers;
                if (headerEof)
                {
                    IsEof = true;
                    return false;
                }
            }
        }

        IsEof = FixedWidthRecordSource.IsEndOfFile(out var rawRecord, out _);
        if (rawRecord.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }

        Record = new RecordMemory(rawRecord.Span, rawRecord.FieldSpans);
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

    private static ReadOnlySpan<char> TrimPadding(ReadOnlySpan<char> span, FixedWidthPadding padding, char paddingChar)
    {
        var start = 0;
        var end = span.Length;
        if (padding is FixedWidthPadding.Left or FixedWidthPadding.Both)
            while (start < end && span[start] == paddingChar)
                start++;
        if (padding is FixedWidthPadding.Right or FixedWidthPadding.Both)
            while (end > start && span[end - 1] == paddingChar)
                end--;
        return span.Slice(start, end - start);
    }
}
