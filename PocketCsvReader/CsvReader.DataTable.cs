using System.Data;

namespace PocketCsvReader;

public partial class CsvReader
{
    /// <summary>Loads a CSV file into a data table using the selected column types.</summary>
    public DataTable ToDataTable(string filename, DataTableColumnTypes columnTypes)
    {
        using var reader = ToDataReader(filename);
        return DataReaderMaterializer.ToDataTable(reader, columnTypes);
    }

    /// <summary>Loads and disposes a CSV stream using the selected column types.</summary>
    public DataTable ToDataTable(Stream stream, DataTableColumnTypes columnTypes)
    {
        using var reader = ToDataReader(stream);
        return DataReaderMaterializer.ToDataTable(reader, columnTypes);
    }
}
