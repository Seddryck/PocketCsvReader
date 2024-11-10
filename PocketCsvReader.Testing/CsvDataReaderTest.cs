using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;

namespace PocketCsvReader.Testing;

[TestFixture]
public class CsvDataReaderTest
{
    private static MemoryStream CreateStream(string content)
    {
        byte[] byteArray = Encoding.UTF8.GetBytes(content);
        MemoryStream stream = new MemoryStream(byteArray);
        stream.Position = 0;
        return stream;
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void ToDataReader_Financial_CorrectRowsColumns(string filename)
    {
        var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var rowCount = 0;
            var dataReader = reader.ToDataReader(stream);
            while (dataReader.Read()) { rowCount++; }
            Assert.That(dataReader.FieldCount, Is.EqualTo(14));
            Assert.That(rowCount, Is.EqualTo(21));
        }
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void ToDataReader_Financial_CorrectColumnByIndexer(string filename)
    {
        var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var dataReader = reader.ToDataReader(stream);
            while (dataReader.Read())
                Assert.Multiple(() =>
                {
                    Assert.That(dataReader[0], Is.EqualTo("2018"));
                    Assert.That(dataReader[1], Is.EqualTo("7"));
                    Assert.That(dataReader[2], Is.EqualTo("1"));
                    Assert.That(dataReader[13], Does.StartWith("2018-"));
                });
        }
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void ToDataReader_Financial_CorrectColumnWithGetStringIndex(string filename)
    {
        var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var dataReader = reader.ToDataReader(stream);
            while (dataReader.Read())
                Assert.Multiple(() =>
                {
                    Assert.That(dataReader.GetString(0), Is.EqualTo("2018"));
                    Assert.That(dataReader.GetString(1), Is.EqualTo("7"));
                    Assert.That(dataReader.GetString(2), Is.EqualTo("1"));
                    Assert.That(dataReader.GetString(13), Does.StartWith("2018-"));
                });
        }
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void ToDataReader_Financial_CorrectIndexWithGetOrdinal(string filename)
    {
        var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var dataReader = reader.ToDataReader(stream);
            Assert.That(dataReader.Read(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(dataReader.GetOrdinal("Year"), Is.EqualTo(0));
                Assert.That(dataReader.GetOrdinal("Month"), Is.EqualTo(1));
                Assert.That(dataReader.GetOrdinal("Day"), Is.EqualTo(2));
                Assert.That(dataReader.GetOrdinal("UpdateTime"), Is.EqualTo(13));
            });
            Assert.Throws<IndexOutOfRangeException>(() => dataReader.GetOrdinal("foo"));
        }
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void ToDataReader_Financial_CorrectNameWithGetName(string filename)
    {
        var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var dataReader = reader.ToDataReader(stream);
            Assert.That(dataReader.Read(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(dataReader.GetName(0), Is.EqualTo("Year"));
                Assert.That(dataReader.GetName(1), Is.EqualTo("Month"));
                Assert.That(dataReader.GetName(2), Is.EqualTo("Day"));
                Assert.That(dataReader.GetName(13), Is.EqualTo("UpdateTime"));
            });
            Assert.Throws<IndexOutOfRangeException>(() => dataReader.GetName(666));
        }
    }

    [Test]
    [TestCase(@"Resources\PackageAssets.csv")]
    public void ToDataReader_PackageAsset_Successful(string filename)
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
}
