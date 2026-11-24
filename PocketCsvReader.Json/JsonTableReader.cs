using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

/// <summary>Streams a JSON array of rows with column names from the first row or supplied by the caller.</summary>
public sealed class JsonTableReader : FlatFileReader<JsonProfile, JsonTableDataReader>
{
    private readonly string[]? _columnNames;

    public JsonTableReader()
        : this(JsonProfile.Default)
    { }

    public JsonTableReader(JsonProfile profile)
        : base(JsonTableDataReader.ValidateProfile(profile))
    { }

    /// <summary>Creates a reader that uses the supplied column names and reads every inner array as data.</summary>
    /// <param name="profile">The JSON reader profile.</param>
    /// <param name="columnNames">The unique column names in row order, compared ordinally.</param>
    public JsonTableReader(JsonProfile profile, IEnumerable<string> columnNames)
        : this(profile)
    {
        ArgumentNullException.ThrowIfNull(columnNames);
        _columnNames = columnNames.ToArray();
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in _columnNames)
        {
            if (name is null)
                throw new ArgumentException("Column names cannot be null.", nameof(columnNames));
            if (!uniqueNames.Add(name))
                throw new ArgumentException($"Duplicate column name '{name}'.", nameof(columnNames));
        }
    }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override JsonTableDataReader CreateDataReader(Stream stream)
        => new(stream, Profile, _columnNames);
}
