using System.Data;
using PocketCsvReader.FieldParsing;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

/// <summary>Exposes the cells of a JSON table by column name or ordinal.</summary>
public sealed class JsonTableDataReader : BaseDataReader<JsonProfile>
{
    private JsonFieldBuffer? _decodingBuffer;

    public JsonTableDataReader(Stream stream, JsonProfile profile)
        : this(stream, profile, null)
    { }

    internal JsonTableDataReader(Stream stream, JsonProfile profile, string[]? columnNames)
        : base(stream, ValidateProfile(profile), new StringMapper(profile.ParserOptimizations.PoolString))
    {
        // Fields exposes an array, so each reader owns its copy of the supplied names.
        Fields = columnNames?.ToArray();
    }

    internal static JsonProfile ValidateProfile(JsonProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ProjectedProperties is not null || profile.RecordPath is not null || profile.StableFieldLayout)
            throw new ArgumentException("Object projection, record paths, and stable object layouts are unavailable for JSON tables.", nameof(profile));
        return profile;
    }

    public override int FieldCount
        => Fields?.Length ?? throw new InvalidOperationException("The JSON table header has not been read.");

    protected override IRecordSource<JsonProfile> CreateRecordSource(StreamReader reader, JsonProfile profile)
        => new JsonRecordSource(reader, profile, table: true, tableWidth: Fields?.Length);

    protected override bool ReadCore()
    {
        if (Fields is null)
            ReadHeader(ReadSource());
        return !IsEof && ReadRow(ReadSource());
    }

    protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (Fields is null)
            ReadHeader(await ReadSourceAsync(cancellationToken).ConfigureAwait(false));
        return !IsEof && ReadRow(await ReadSourceAsync(cancellationToken).ConfigureAwait(false));
    }

    private void ReadHeader(CurrentRecordResult result)
    {
        Fields = ((JsonRecordSource)RecordSource!).TableHeader!;
        IsEof = result.IsEndOfFile;
        PositionTracker.ExcludeRecord(RecordSource!);
    }

    private bool ReadRow(CurrentRecordResult result)
    {
        IsEof = result.IsEndOfFile;
        SetRecord(result.Record);
        if (result.State == RecordState.Eof)
            return false;
        RowCount++;
        return true;
    }

    public override string GetRawString(int i)
    {
        var value = CurrentField(i).Value;
        var quoteLength = value.WasQuoted ? 1 : 0;
        return CurrentRecordMemory.Slice(value.Start - quoteLength, value.Length + (2 * quoteLength)).ToString();
    }

    protected override bool IsNull(int i) => CurrentField(i).Value.IsNull;

    protected override object GetNullValue(int i) => DBNull.Value;

    protected override NullableSpan GetValueOrThrow(int i)
    {
        ref var field = ref CurrentField(i);
        if (field.Value.IsNull)
            return default;
        return GetFieldMemory(ref field);
    }

    protected override ReadOnlyMemory<char> GetFieldMemory(ref FieldSpan field)
        => JsonValueDecoder.GetMemory(CurrentRecordMemory, ref field, ref _decodingBuffer);

    private ref FieldSpan CurrentField(int ordinal)
    {
        var fields = CurrentFieldSpans ?? throw new InvalidOperationException("Current record is not set.");
        if ((uint)ordinal >= (uint)fields.Length)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return ref fields[ordinal];
    }
}
