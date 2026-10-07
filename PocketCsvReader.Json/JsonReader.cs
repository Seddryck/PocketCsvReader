using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

public class JsonReader : FlatFileReader<JsonProfile, JsonDataReader>
{
    public JsonReader()
        : this(JsonProfile.Default)
    { }

    public JsonReader(JsonProfile profile)
        : base(profile)
    { }

    public JsonReader(int bufferSize)
        : this(JsonProfile.Default, bufferSize)
    { }

    public JsonReader(JsonProfile profile, int bufferSize)
        : this(WithBufferSize(profile, bufferSize))
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override JsonDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);

    private static JsonProfile WithBufferSize(JsonProfile profile, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "The parser buffer size must be positive.");

        return new JsonProfile(
            profile.Schema,
            profile.Resource,
            profile.Parsers,
            profile.ParserOptimizations with { BufferSize = bufferSize },
            profile.ProjectedProperties);
    }
}
