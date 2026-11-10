using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

public class NdjsonReader : FlatFileReader<NdjsonProfile, NdjsonDataReader>
{
    public event ProgressStatusHandler? ProgressStatusChanged;

    protected IEncodingDetector EncodingDetector { get; set; } = new EncodingDetector();
    public NdjsonDialectDescriptor Dialect => Profile.Dialect;
    protected int BufferSize => Profile.ParserOptimizations.BufferSize;

    public NdjsonReader()
        : this(NdjsonProfile.Default)
    { }

    public NdjsonReader(NdjsonProfile profile)
        : base(profile)
    { }

    public NdjsonReader(int bufferSize)
        : this(NdjsonProfile.Default, bufferSize)
    { }

    public NdjsonReader(NdjsonProfile profile, int bufferSize)
        : this(WithBufferSize(profile, bufferSize))
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    internal override IEnumerable<string?>? SeekConfiguration
        => GetType() == typeof(NdjsonReader) && Profile.RecordPath is null && !Profile.StableFieldLayout
            ? ["ndjson", Dialect.LineTerminator, Dialect.CommentChar?.ToString(), new string(Dialect.Whitespaces),
                Profile.OrderedProjection.ToString(), Profile.ProjectedProperties is null ? null : "projection",
                .. Profile.ProjectedProperties ?? []]
            : null;

    protected override NdjsonDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);

    protected void RaiseProgressStatus(string status)
        => ProgressStatusChanged?.Invoke(this, new ProgressStatusEventArgs(status));

    protected void RaiseProgressStatus(string status, int current, int total)
        => ProgressStatusChanged?.Invoke(
            this,
            new ProgressStatusEventArgs(string.Format(status, current, total), current, total));

    private static NdjsonProfile WithBufferSize(NdjsonProfile profile, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "The parser buffer size must be positive.");

        return new NdjsonProfile(
            profile.Dialect,
            profile.Schema,
            profile.Resource,
            profile.Parsers,
            profile.ParserOptimizations with { BufferSize = bufferSize },
            profile.ProjectedProperties,
            profile.OrderedProjection,
            profile.StableFieldLayout,
            profile.RecordPath) { Filters = profile.Filters, Skip = profile.Skip, Take = profile.Take };
    }
}
