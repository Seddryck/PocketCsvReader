using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace PocketCsvReader;

public partial class CsvReader
{
    /// <summary>Reads decoded CSV characters from the current position of a text reader.</summary>
    /// <param name="reader">The forward-only character input.</param>
    /// <param name="leaveOpen">Whether disposing the CSV reader leaves the text reader open.</param>
    /// <remarks>The CSV reader buffers input and may consume characters beyond the last record returned.</remarks>
    public CsvDataReader ToDataReader(TextReader reader, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new CsvDataReader(reader, Profile, leaveOpen);
    }

    /// <summary>Exposes decoded CSV input as a standard database reader.</summary>
    /// <param name="reader">The forward-only character input.</param>
    /// <param name="leaveOpen">Whether disposing the database reader leaves the text reader open.</param>
    public DbDataReader ToDbDataReader(TextReader reader, bool leaveOpen = true)
        => new DbDataReaderAdapter(ToDataReader(reader, leaveOpen));

    /// <summary>Loads the CSV records from a text reader into a data table.</summary>
    /// <param name="reader">The forward-only character input.</param>
    /// <param name="leaveOpen">Whether to leave the text reader open after loading the table.</param>
    public DataTable ToDataTable(TextReader reader, bool leaveOpen = true)
    {
        using var csv = ToDataReader(reader, leaveOpen);
        return DataReaderMaterializer.ToDataTable(csv);
    }

    /// <summary>Lazily reads CSV records as arrays of strings.</summary>
    public IEnumerable<string?[]> ToArrayString(TextReader reader, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return MaterializeStringArrays(() => ToDataReader(reader, leaveOpen));
    }

    /// <summary>Lazily reads CSV records as dynamic objects keyed by field name.</summary>
    public IEnumerable<dynamic> ToDynamic(TextReader reader, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return MaterializeDynamic(() => ToDataReader(reader, leaveOpen));
    }

    /// <summary>Lazily reads CSV records as dictionaries keyed by field name.</summary>
    public IEnumerable<IReadOnlyDictionary<string, object?>> ToDictionaries(TextReader reader, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return MaterializeDictionaries(() => ToDataReader(reader, leaveOpen));
    }

    /// <summary>Lazily reads CSV records as arrays of parsed field values.</summary>
    public IEnumerable<object?[]> ToObjectArrays(TextReader reader, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return MaterializeObjectArrays(() => ToDataReader(reader, leaveOpen));
    }

    /// <summary>Lazily maps CSV records from a text reader to objects of type <typeparamref name="T"/>.</summary>
    /// <param name="reader">The forward-only character input.</param>
    /// <param name="spanMapper">An optional mapper. When omitted, fields are mapped by constructor order.</param>
    /// <param name="leaveOpen">Whether to leave the text reader open when enumeration ends.</param>
    public IEnumerable<T> To<T>(TextReader reader, SpanMapper<T>? spanMapper = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return ReadObjects();

        IEnumerable<T> ReadObjects()
        {
            using var csv = ToDataReader(reader, leaveOpen);
            var mapper = spanMapper ?? new SpanMapper<T>(new SpanObjectBuilder<T>().Instantiate);
            while (csv.Read())
                yield return csv.MapCurrent(mapper, defaultMapper: spanMapper is null);
        }
    }

    /// <summary>Asynchronously maps CSV records from a text reader to objects of type <typeparamref name="T"/>.</summary>
    /// <param name="reader">The forward-only character input.</param>
    /// <param name="spanMapper">An optional mapper. When omitted, fields are mapped by constructor order.</param>
    /// <param name="leaveOpen">Whether to leave the text reader open when enumeration ends.</param>
    /// <param name="cancellationToken">A token used to cancel asynchronous reads.</param>
    public async IAsyncEnumerable<T> ToAsync<T>(
        TextReader reader,
        SpanMapper<T>? spanMapper = null,
        bool leaveOpen = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var csv = ToDataReader(reader, leaveOpen);
        var mapper = spanMapper ?? new SpanMapper<T>(new SpanObjectBuilder<T>().Instantiate);
        while (await csv.ReadAsync(cancellationToken).ConfigureAwait(false))
            yield return csv.MapCurrent(mapper, defaultMapper: spanMapper is null);
    }
}
