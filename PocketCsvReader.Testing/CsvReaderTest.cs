using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
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

    private record struct PackageAsset(
        string Guid, DateTimeOffset Created, string Name, string Version,
        DateTimeOffset Updated, string Description, string Runtime,
        string Field1, string Field2, string Field3, string Field4, string Field5,
        string Field6, string Field7, string Field8, string Field9, string Field10,
        string Field11, string Field12, string Field13, string Field14, string Field15,
        string Field16, string Field17, string Field18
        );

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToObjectWithSpanMapper_PackageAssetStream_Successful(string filename)
    {
        var objBuilder = new SpanObjectBuilder<PackageAsset>();
        objBuilder.SetParser<DateTimeOffset>(s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture));
        var spanMapper = new SpanMapper<PackageAsset>(objBuilder.Instantiate);

        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var arrays = new CsvReader(profile).To(stream, spanMapper);
        Assert.That(arrays.Count, Is.EqualTo(1695));
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToObject_PackageAssetStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var arrays = new CsvReader(profile).To<PackageAsset>(stream);
        Assert.That(arrays.Count, Is.EqualTo(1695));
    }
}
