using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

/// <summary>Streams a JSON array of row arrays whose first row supplies column names.</summary>
public sealed class JsonTableReader : FlatFileReader<JsonProfile, JsonTableDataReader>
{
    public JsonTableReader()
        : this(JsonProfile.Default)
    { }

    public JsonTableReader(JsonProfile profile)
        : base(JsonTableDataReader.ValidateProfile(profile))
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override JsonTableDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);
}
