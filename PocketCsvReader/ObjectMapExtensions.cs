namespace PocketCsvReader;

/// <summary>Reads CSV rows using named object property maps.</summary>
public static class ObjectMapExtensions
{
    /// <summary>Maps CSV rows to objects by header name using the reader's typed field access.</summary>
    /// <remarks>Enumeration opens and disposes the file. For a keyed map, enumeration updates and yields each resolved target.</remarks>
    /// <exception cref="InvalidDataException">A mapped header is missing or occurs more than once.</exception>
    public static IEnumerable<T> To<T>(this CsvReader csv, string filename, ObjectMap<T> map)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentNullException.ThrowIfNull(map);
        return Read();

        IEnumerable<T> Read()
        {
            using var reader = csv.ToDataReader(filename);
            foreach (var item in map.Read(reader))
                yield return item;
        }
    }
}
