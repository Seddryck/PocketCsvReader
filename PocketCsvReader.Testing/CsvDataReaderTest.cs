using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;

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
    public void GetString_SingleFieldAttemptForSecond_Throws()
    {
        var profile = new CsvProfile(',', '\"', "\r\n", false);
        profile.ParserOptimizations = new ParserOptimizationOptions()
        {
            ExtendIncompleteRecords = false,
        };
        var reader = new CsvReader(profile);

        var stream = CreateStream("foo,bar\r\nfoo\r\nfoo,bar");
        var dataReader = reader.ToDataReader(stream);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        var ex = Assert.Throws<IndexOutOfRangeException>(() => dataReader.GetString(1));
        Assert.That(ex!.Message, Does.Contain("record '2'"));
        Assert.That(ex.Message, Does.Contain("index '1'"));
        Assert.That(ex.Message, Does.Contain("contains 1 defined fields"));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));
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

    [TestCase(40_000, true)]
    [TestCase(40_000, false)]
    public void ToDataReader_TestData_Successful(int lineCount, bool handleSpecialValues)
    {
        var bytes = TestData.PackageAssets.GetBytes(lineCount);
        using (var memoryStream = new MemoryStream(bytes, writable: false))
        {
            var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
            profile.ParserOptimizations = new ParserOptimizationOptions()
            {
                NoTextQualifier = true,
                UnescapeChars = false,
                HandleSpecialValues = handleSpecialValues,
            };
            var reader = new CsvReader(profile).ToDataReader(memoryStream);

            var rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
                if (rowCount == 3827)
                    Console.WriteLine(reader.GetString(0));
                Assert.That(reader.FieldCount, Is.EqualTo(25));
                for (var i = 0; i < reader.FieldCount; i++)
                    reader.GetString(i);
            }
            Assert.That(rowCount, Is.EqualTo(lineCount));
        }
    }

    [TestCase(40_000, true)]
    [TestCase(40_000, false)]
    public void ToDataReader_TestData_CompareBasicParser(int lineCount, bool readAhead)
    {
        var reference = new List<string[]>();
        var bytes = TestData.PackageAssets.GetBytes(lineCount);
        using (var memoryStream = new MemoryStream(bytes, writable: false))
        {
            var reader = new StreamReader(memoryStream);
            var data = reader.ReadLine();
            while (data != null)
            {
                reference.Add(data.Split(','));
                data = reader.ReadLine();
            }
        }

        using (var memoryStream = new MemoryStream(bytes, writable: false))
        {
            var profile = new CsvProfile(',', '\"', Environment.NewLine, false);
            profile.ParserOptimizations = new ParserOptimizationOptions()
            {
                NoTextQualifier = true,
                UnescapeChars = false,
                HandleSpecialValues = false,
                ReadAhead = readAhead
            };
            var reader = new CsvReader(profile).ToDataReader(memoryStream);

            var rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
                Assert.That(reader.FieldCount, Is.EqualTo(25));
                for (var i = 0; i < reader.FieldCount; i++)
                    Assert.That(reader.GetString(i), Is.EqualTo(reference[rowCount - 1][i]), $"Row {rowCount}, record {i}: {reader.GetString(i)}");
            }
            Assert.That(rowCount, Is.EqualTo(lineCount));
        }
    }
}
