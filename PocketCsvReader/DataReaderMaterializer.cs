using System.Data;

namespace PocketCsvReader;

internal static class DataReaderMaterializer
{
    public static DataTable ToDataTable(IDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var table = new DataTable();
        if (!reader.Read())
            return table;

        for (var index = 0; index < reader.FieldCount; index++)
            table.Columns.Add(reader.GetName(index));

        do
        {
            var row = table.NewRow();
            for (var index = 0; index < reader.FieldCount; index++)
                row[index] = reader.IsDBNull(index) ? DBNull.Value : reader.GetString(index);
            table.Rows.Add(row);
        } while (reader.Read());

        return table;
    }

    public static IEnumerable<string?[]> ToStringArrays(IDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        while (reader.Read())
        {
            var values = new string?[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
                values[index] = reader.IsDBNull(index) ? null : reader.GetString(index);
            yield return values;
        }
    }
}
