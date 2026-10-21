using System.Data;
using System.Data.Common;

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

    /// <summary>Opens a flat file as a standard <see cref="DbDataReader"/>.</summary>
    public DbDataReader ToDbDataReader(string filename)
        => Adapt(ToDataReader(filename));

    /// <summary>Reads a flat-file stream as a standard <see cref="DbDataReader"/>.</summary>
    public DbDataReader ToDbDataReader(Stream stream)
        => Adapt(ToDataReader(stream));

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

    /// <summary>Lazily reads independently owned dictionaries keyed by the reader's field names.</summary>
    /// <remarks>Values use the configured parsers; database nulls become null. Duplicate names
    /// (compared ordinally) throw InvalidDataException. Enumeration owns and disposes the reader.</remarks>
    public IEnumerable<IReadOnlyDictionary<string, object?>> ToDictionaries(string filename)
    {
        CheckFileExists(filename);
        return MaterializeDictionaries(() => ToDataReader(filename));
    }

    /// <inheritdoc cref="ToDictionaries(string)"/>
    public IEnumerable<IReadOnlyDictionary<string, object?>> ToDictionaries(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return MaterializeDictionaries(() => ToDataReader(stream));
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> MaterializeDictionaries(Func<TDataReader> createReader)
    {
        using var reader = createReader();
        foreach (var row in DataReaderMaterializer.ToDictionaries(reader))
            yield return row;
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

    private static DbDataReader Adapt(TDataReader reader)
    {
        if (reader is IAsyncDataReader asyncReader)
            return new DbDataReaderAdapter(asyncReader);

        reader.Dispose();
        throw new NotSupportedException($"{typeof(TDataReader).Name} must implement {nameof(IAsyncDataReader)} to be exposed as a DbDataReader.");
    }
}
