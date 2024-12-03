using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Buffers;
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
    public void GetString_SingleFieldAttemptForSecond_Throws()
    {
        var profile = new CsvProfile(',', '\"', "\r\n", false);
        profile.ParserOptimizations = new ParserOptimizationOptions()
        {
            ExtendIncompleteRecords = false,
        };
        using var stream = CreateStream("foo,bar\r\nfoo\r\nfoo,bar");
        using var dataReader = new CsvDataReader(stream, profile);

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        var ex = Assert.Throws<IndexOutOfRangeException>(() => dataReader.GetString(1));
        //Assert.That(ex!.Message, Does.Contain("record '2'"));
        Assert.That(ex.Message, Does.Contain("index '1'"));
        Assert.That(ex.Message, Does.Contain("contains 1 defined fields"));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));
    }

    [TestCase("'ab'';''c';'xyz'", "ab';'c")]
    [TestCase("'ab'';''''c';'xyz'", "ab';''c")]
    [TestCase("'a''b'';c';'xyz'", "a'b';c")]
    public void GetString_RecordWithTwoFields_CorrectParsing(string record, string firstToken)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
            new CsvDialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.GetString(0), Is.EqualTo(firstToken));
        Assert.That(dataReader.GetString(1), Is.EqualTo("xyz"));
    }

    [Test]
    public void GetInt32_RecordWithTwoFields_CorrectParsing()
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes("foo;17"));

        var profile = new CsvProfile(
            new CsvDialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetInt32(1), Is.EqualTo(17));
    }

    [Test]
    public void GetDecimal_RecordWithTwoFields_CorrectParsing()
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes("foo;17.02542"));

        var profile = new CsvProfile(
            new CsvDialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetDecimal(1), Is.EqualTo(17.02542m));
    }

    [Test]
    public void GetDateTime_RecordWithTwoFields_CorrectParsing()
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes("foo;2024-12-06T12:45:16"));

        var profile = new CsvProfile(
            new CsvDialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetDateTime(1), Is.EqualTo(new DateTime(2024, 12, 06, 12, 45, 16)));
    }

    [Test]
    [TestCase("'fo\\'o'", '\\')]
    [TestCase("'fo?'o'", '?')]
    public void ReadNextRecord_SingleFieldWithTextEscaper_CorrectParsing(string record, char escapeTextQualifier)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', escapeTextQualifier, "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.That(dataReader.GetString(0), Is.EqualTo("fo'o"));
    }

    [Test]
    [TestCase("'fo''o'")]
    public void ReadNextRecord_SingleFieldWithDoubleQuote_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
                new CsvDialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false }
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.That(dataReader.GetString(0), Is.EqualTo("fo'o"));
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void Read_Financial_CorrectRowsColumns(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var rowCount = 0;
            using var dataReader = new CsvDataReader(stream, profile);
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
    public void Read_Financial_CorrectColumnByIndexer(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            using var dataReader = new CsvDataReader(stream, profile);
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
    public void GetString_Financial_CorrectColumnWithGetStringIndex(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            using var dataReader = new CsvDataReader(stream, profile);
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
    public void GetOrdinal_Financial_CorrectIndexWithGetOrdinal(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            using var dataReader = new CsvDataReader(stream, profile);
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
    public void Read_Financial_CorrectNameWithGetName(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            using var dataReader = new CsvDataReader(stream, profile);
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

    [TestCase(40_000, true)]
    [TestCase(40_000, false)]
    public void Read_TestData_Successful(int lineCount, bool handleSpecialValues)
    {
        var bytes = TestData.PackageAssets.GetBytes(lineCount);
        using (var memoryStream = new MemoryStream(bytes, writable: false))
        {
            var profile = new CsvProfile(',', '\"', Environment.NewLine, false)
            {
                ParserOptimizations = new ParserOptimizationOptions()
                {
                    NoTextQualifier = true,
                    UnescapeChars = false,
                    HandleSpecialValues = handleSpecialValues,
                }
            };
            var dataReader = new CsvDataReader(memoryStream, profile);

            var rowCount = 0;
            while (dataReader.Read())
            {
                rowCount++;
                Assert.That(dataReader.FieldCount, Is.EqualTo(25));
                for (var i = 0; i < dataReader.FieldCount; i++)
                    dataReader.GetString(i);
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
            var profile = new CsvProfile(',', '\"', Environment.NewLine, false)
            {
                ParserOptimizations = new ParserOptimizationOptions()
                {
                    NoTextQualifier = true,
                    UnescapeChars = false,
                    HandleSpecialValues = false,
                    ReadAhead = readAhead
                }
            };
            var dataReader = new CsvDataReader(memoryStream, profile);

            var rowCount = 0;
            while (dataReader.Read())
            {
                rowCount++;
                Assert.That(dataReader.FieldCount, Is.EqualTo(25));
                for (var i = 0; i < dataReader.FieldCount; i++)
                    Assert.That(dataReader.GetString(i), Is.EqualTo(reference[rowCount - 1][i]), $"Row {rowCount}, record {i}: {dataReader.GetString(i)}");
            }
            Assert.That(rowCount, Is.EqualTo(lineCount));
        }
    }
}
