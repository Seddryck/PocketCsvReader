using System.Data;

namespace PocketCsvReader;

public abstract class FlatFileReader<TProfile, TDataReader>
    where TProfile : IProfile
    where TDataReader : IDataReader
{
    protected internal TProfile Profile { get; }

    protected FlatFileReader(TProfile profile)
        => Profile = profile ?? throw new ArgumentNullException(nameof(profile));

    protected abstract int StreamBufferSize { get; }

    protected abstract TDataReader CreateDataReader(Stream stream);

    public TDataReader ToDataReader(string filename)
        => CreateDataReader(OpenRead(filename));

    public TDataReader ToDataReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return CreateDataReader(stream);
    }

    public DataTable ToDataTable(string filename)
    {
        using var reader = ToDataReader(filename);
        return DataReaderMaterializer.ToDataTable(reader);
    }

    public DataTable ToDataTable(Stream stream)
    {
        using var reader = ToDataReader(stream);
        return DataReaderMaterializer.ToDataTable(reader);
    }

    public IEnumerable<string?[]> ToArrayString(string filename)
    {
        CheckFileExists(filename);
        return MaterializeStringArrays(() => ToDataReader(filename));
    }

    public IEnumerable<string?[]> ToArrayString(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return MaterializeStringArrays(() => ToDataReader(stream));
    }

    protected Stream OpenRead(string filename)
    {
        CheckFileExists(filename);
        return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize);
    }

    protected virtual void CheckFileExists(string filename)
    {
        if (!File.Exists(filename))
            throw new FileNotFoundException($"The file {filename} was not found.", filename);
    }

    private static IEnumerable<string?[]> MaterializeStringArrays(Func<TDataReader> createReader)
    {
        using var reader = createReader();
        foreach (var values in DataReaderMaterializer.ToStringArrays(reader))
            yield return values;
    }
}
