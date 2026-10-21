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

        var fieldCount = reader.FieldCount;
        for (var index = 0; index < fieldCount; index++)
            table.Columns.Add(reader.GetName(index));

        do
        {
            var row = table.NewRow();
            for (var index = 0; index < fieldCount; index++)
                row[index] = reader.IsDBNull(index) ? DBNull.Value : reader.GetString(index);
            table.Rows.Add(row);
        } while (reader.Read());

        return table;
    }

    public static IEnumerable<IReadOnlyDictionary<string, object?>> ToDictionaries(IDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var name = reader.GetName(index);
                if (!row.TryAdd(name, ReadValue(reader, index)))
                    throw new InvalidDataException($"Duplicate field name '{name}' cannot be materialized as a dictionary.");
            }
            yield return row;
        }
    }

    private static object? ReadValue(IDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
            return null;
        var value = reader.GetValue(index);
        return value is DBNull ? null : value;
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
