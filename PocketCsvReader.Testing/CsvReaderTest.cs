using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class CsvReaderTest
{
    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToDataReader_PackageAssetFile_Successful(string filename)
    {
        var rowCount = 0;
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var reader = new CsvReader(profile).ToDataReader(filename);
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(1695));
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToDataReader_PackageAssetStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var rowCount = 0;
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var reader = new CsvReader(profile).ToDataReader(stream);
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(1695));
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToDataTable_PackageAssetFile_Successful(string filename)
    {
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var dataTable = new CsvReader(profile).ToDataTable(filename);
        Assert.That(dataTable.Rows.Count, Is.EqualTo(1695));
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void Read_PackageAssetStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var dataTable = new CsvReader(profile).ToDataTable(stream);
        Assert.That(dataTable.Rows.Count, Is.EqualTo(1695));
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToArrayOfString_PackageAssetFile_Successful(string filename)
    {
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var arrays = new CsvReader(profile).ToArrayString(filename);
        Assert.That(arrays.Count, Is.EqualTo(1695));
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToArrayOfString_PackageAssetStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var arrays = new CsvReader(profile).ToArrayString(stream);
        Assert.That(arrays.Count, Is.EqualTo(1695));
    }
}
