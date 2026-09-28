using System.Data;
using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class DataReaderMaterializerTest
{
    [Test]
    public void ToDataTable_MaterializesNamesValuesAndNulls()
    {
        var source = CreateTable();
        using var reader = source.CreateDataReader();

        var result = DataReaderMaterializer.ToDataTable(reader);

        Assert.Multiple(() =>
        {
            Assert.That(result.Columns.Cast<DataColumn>().Select(column => column.ColumnName),
                Is.EqualTo(new[] { "Id", "Name" }));
            Assert.That(result.Rows.Count, Is.EqualTo(2));
            Assert.That(result.Rows[0].ItemArray, Is.EqualTo(new object[] { "1", "Ada" }));
            Assert.That(result.Rows[1].IsNull("Name"), Is.True);
        });
    }

    [Test]
    public void ToStringArrays_MaterializesValuesAndNulls()
    {
        var source = CreateTable();
        using var reader = source.CreateDataReader();

        var result = DataReaderMaterializer.ToStringArrays(reader).ToArray();

        Assert.That(result, Is.EqualTo(new[]
        {
            new string?[] { "1", "Ada" },
            new string?[] { "2", null }
        }));
    }

    private static DataTable CreateTable()
    {
        var table = new DataTable();
        table.Columns.Add("Id");
        table.Columns.Add("Name");
        table.Rows.Add("1", "Ada");
        table.Rows.Add("2", DBNull.Value);
        return table;
    }
}
