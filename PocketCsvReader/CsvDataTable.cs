using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace PocketCsvReader;
public class CsvDataTable
{
    protected CsvProfile Profile { get; }
    protected Stream Stream { get; }
    protected Memory<char> buffer;

    public CsvDataTable(Stream stream, CsvProfile profile)
    {
        Profile = profile;
        Stream = stream;
    }

    public DataTable CreateTable(string[] headers)
    {
        var table = new DataTable();
        foreach (var header in headers)
            table.Columns.Add(header);
        return table;
    }

    public DataTable Read()
    {
        int i = 0;
        var table = new DataTable();

        using (var dataReader = new CsvDataReader(Stream, Profile))
        {
            while (dataReader.Read())
            {
                if (i == 0)
                    table = CreateTable(dataReader.Fields!);

                i++;
                var row = table.NewRow();

                for (int j = 0; j < dataReader.Fields!.Length; j++)
                    row[j] = dataReader.GetString(j);

                table.Rows.Add(row);
            }

            return table;
        }
    }
}
