using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using NUnit.Framework;
using PocketCsvReader.Configuration;

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
    public void ToDataReader_PackageAssetFileTwice_Successful(string filename)
    {
        var rowCount = 0;
        var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
        var reader = new CsvReader(profile).ToDataReader([filename, filename]);
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(1695 * 2));
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

    [Test]
    [TestCase(@"Resources\data-journalism-university-courses.csv")]
    public void ToDataReader_DataJournalismUniversityCoursesStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(',', '\"', Environment.NewLine, true);
        using var reader = new CsvReader(profile).ToDataReader(stream);
        for (int i = 0; i < 50; i++)
        {
            Assert.That(reader.Read(), Is.True);
            for (var j = 0; j < reader.FieldCount; j++)
            {
                var value = reader.GetValue(j);
                Assert.That(value, Is.Not.Null);
            }
        }
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    [TestCase(@"Resources\natural-gas-monthly.csv")]
    public void ToDataReader_NaturalGasMonthlyStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(',', '\"', Environment.NewLine, true);
        using var reader = new CsvReader(profile).ToDataReader(stream);
        for (int i = 0; i < 336; i++)
        {
            Assert.That(reader.Read(), Is.True);
            for (int j = 0; j < reader.FieldCount; j++)
            {
                var value = reader.GetValue(j);
                Assert.That(value, Is.Not.Null);
            }
        }
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    [TestCase(@"Resources\natural-gas-monthly.csv")]
    public void ToDataReader_NaturalGasMonthlyStreamWithSchema_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithQuoteChar('\"')
            .WithLineTerminator(Environment.NewLine)
            .WithHeader()
            .Build(),
            new SchemaDescriptorBuilder()
            .Named()
            .WithTemporalField<string>("Month")
            .WithNumberField<decimal>("Price")
            .Build());
        using var reader = new CsvReader(profile).ToDataReader(stream);
        for (int i = 0; i < 336; i++)
        {
            Assert.That(reader.Read(), Is.True);
            for (int j = 0; j < reader.FieldCount; j++)
            {
                var value = reader.GetValue(j);
                Assert.That(value, Is.Not.Null);
            }
        }
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    [TestCase(@"Resources\language.csv.gz")]
    public void ToDataReader_LanguageWithCompression_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\n")
            .WithHeader()
            .Build(),
            null,
            new ResourceDescriptorBuilder()
            .WithEncoding("utf-8")
            .WithCompression("gz")
            .Build());
        using var reader = new CsvReader(profile).ToDataReader(stream);
        for (int i = 0; i < 2; i++)
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(i+1));
            Assert.That(reader.GetString(1), Is.EqualTo("english").Or.EqualTo("中国人"));
        }
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    [TestCase(@"Resources\cars-2018.csv.zip")]
    public void ToDataReader_CarsWithCompression_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\n")
            .WithHeader()
            .Build(),
            null,
            new ResourceDescriptorBuilder()
            .WithCompression(CompressionFormat.Zip)
            .Build());
        using var reader = new CsvReader(profile).ToDataReader(stream);
        for (int i = 0; i < 3; i++)
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.Not.Null.Or.Empty);
            Assert.That(reader.GetInt32(1), Is.EqualTo(2018));
        }
        Assert.That(reader.Read(), Is.False);
    }
}
