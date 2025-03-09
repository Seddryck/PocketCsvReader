using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Reflection;

namespace PocketCsvReader.Testing;

[TestFixture]
public class CsvDataTableTest
{
    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void ToDataTable_Financial_CorrectRowsColumns(string filename)
    {
        var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var dataTable = reader.ToDataTable(stream);
            Assert.That(dataTable.Columns.Count, Is.EqualTo(14));
            Assert.That(dataTable.Rows.Count, Is.EqualTo(21));
        }
    }

    [Test]
    [TestCase("a+b+c#a+b#a#a+b", '+', "#", "?")]
    public void Read_CsvWithCsvProfileMissingCell_CorrectResults(string text, char fieldSeparator, string recordSeparator, string missingCell)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();

            stream.Position = 0;

            var profile = new CsvProfile(fieldSeparator, '`', '`', recordSeparator, false, true, 4096, "_", missingCell);
            var reader = new CsvReader(profile);
            var dataTable = reader.ToDataTable(stream);

            Assert.That(dataTable.Rows[0].ItemArray[0], Is.EqualTo("a"));
            Assert.That(dataTable.Rows[0].ItemArray[1], Is.EqualTo("b"));
            Assert.That(dataTable.Rows[0].ItemArray[2], Is.EqualTo("c"));

            Assert.That(dataTable.Rows[1].ItemArray[0], Is.EqualTo("a"));
            Assert.That(dataTable.Rows[1].ItemArray[1], Is.EqualTo("b"));
            Assert.That(dataTable.Rows[1].ItemArray[2], Is.EqualTo("?"));

            Assert.That(dataTable.Rows[2].ItemArray[0], Is.EqualTo("a"));
            Assert.That(dataTable.Rows[2].ItemArray[1], Is.EqualTo("?"));
            Assert.That(dataTable.Rows[2].ItemArray[2], Is.EqualTo("?"));

            Assert.That(dataTable.Rows[3].ItemArray[0], Is.EqualTo("a"));
            Assert.That(dataTable.Rows[3].ItemArray[1], Is.EqualTo("b"));
            Assert.That(dataTable.Rows[3].ItemArray[2], Is.EqualTo("?"));


            writer.Dispose();
        }
    }

    [Test]
    [TestCase("a+b+c#a++c", '+', "#", "?", "a", "?", "c")]
    [TestCase("a+b+c#+b+c", '+', "#", "?", "?", "b", "c")]
    [TestCase("a+b+c#+b+", '+', "#", "?", "?", "b", "?")]
    public void Read_CsvWithCsvProfileEmptyCell_CorrectResults(string text, char fieldSeparator, string recordSeparator, string emptyCell, params string[] expected)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();

            stream.Position = 0;
            var profile = new CsvProfile(fieldSeparator, '`', '`', recordSeparator, false, true, 4096, emptyCell, "_");
            var reader = new CsvReader(profile);
            var dataTable = reader.ToDataTable(stream);

            Assert.That(dataTable.Rows[0].ItemArray[0], Is.EqualTo("a"));
            Assert.That(dataTable.Rows[0].ItemArray[1], Is.EqualTo("b"));
            Assert.That(dataTable.Rows[0].ItemArray[2], Is.EqualTo("c"));

            for (int i = 0; i < 3; i++)
                Assert.That(dataTable.Rows[1].ItemArray[i], Is.EqualTo(expected[i]));

            writer.Dispose();
        }
    }

    [Test]
    [TestCase("abc\r\ndef\r\nghl\r\nijk", 1, 1)]
    [TestCase("abc\r\ndef\r\nghl\r\nijk", 17, 1)]
    [TestCase("abc\r\ndef\r\nghl\r\nijk", 18, 1)]
    [TestCase("abc\r\ndef\r\nghl\r\nijk", 19, 1)]
    [TestCase("abc\r\ndef\r\nghl\r\nijk", 512, 1)]
    [TestCase("abc;xyz\r\ndef;xyz\r\nghl\r\n;ijk", 1, 2)]
    [TestCase("abc;xyz\r\ndef;xyz\r\nghl\r\n;ijk", 512, 2)]
    [TestCase("\"abc\";\"xyz\"\r\n\"def\";\"xyz\"\r\n\"ghl\"\r\n;\"ijk\"", 512, 2)]
    [TestCase("abc;\"xyz\"\r\n\"def\";xyz\r\n\"ghl\"\r\n;\"ijk\"", 512, 2)]
    [TestCase("abc;\"xyz\"\r\n\"def\";xyz\r\n\"ghl\"\r\n;\"ijk\"", 512, 2)]
    public void Read_Csv_CorrectResult(string text, int bufferSize, int fieldCount)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var reader = new CsvReader(new CsvProfile(';', '\"', '\\', "\r\n", false, false, 4096, "(empty)", "(null)"), bufferSize);
        var dataTable = reader.ToDataTable(stream);
        Assert.That(dataTable.Rows, Has.Count.EqualTo(4));
        Assert.That(dataTable.Columns, Has.Count.EqualTo(fieldCount));
        foreach (DataRow row in dataTable.Rows)
        {
            foreach (var cell in row.ItemArray)
                Assert.That(cell!.ToString(), Has.Length.EqualTo(3).Or.EqualTo("(empty)").Or.EqualTo("(null)"));
        }
        Assert.That(dataTable.Rows[0][0], Is.EqualTo("abc"));
        if (dataTable.Columns.Count == 2)
            Assert.That(dataTable.Rows[0][1], Is.EqualTo("xyz"));
    }

    [Test]
    [TestCase("'azerty';'';'alpha'", 3)]
    [TestCase("'azerty';;'alpha'", 3)]
    public void Read_CsvWithTextQualifier_CorrectResult(string text, int columnCount)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var reader = new CsvReader(new CsvProfile(';', '\'', '\\', "\r\n", false, false, 4096, "foo", "(null)"));
        var dataTable = reader.ToDataTable(stream);
        Assert.That(dataTable.Columns, Has.Count.EqualTo(columnCount));
        Assert.That(dataTable.Rows[0][0], Is.EqualTo("azerty"));
        Assert.That(dataTable.Rows[0][1], Is.EqualTo("foo"));
        Assert.That(dataTable.Rows[0][2], Is.EqualTo("alpha"));
    }

    [Test]
    [TestCase("a;b;c\r\nd;e;f;g\r\n", 2, 1)]
    [TestCase("a;b;c\r\nd;e;f\r\ng;h;i;j\r\n", 3, 1)]
    [TestCase("a;b;c\r\nd;e;f\r\ng;h;i;j;k\r\n", 3, 2)]
    public void Read_MoreFieldThanExpected_ExceptionMessage(string text, int rowNumber, int moreField)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var profile = CsvProfile.SemiColumnDoubleQuote;
        var reader = new CsvReader(profile, 1024);

        var ex = Assert.Throws<InvalidDataException>(() => reader.ToDataTable(stream));
        Assert.That(ex!.Message, Does.Contain(string.Format("record {0} ", rowNumber)));
        Assert.That(ex.Message, Does.Contain(string.Format("{0} more", moreField)));
    }

    [Test]
    public void Read_EmptyValue_MatchWithEmpty()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a;;c"));
        var profile = CsvProfile.SemiColumnDoubleQuote;
        var reader = new CsvReader(profile);
        var dataTable = reader.ToDataTable(stream);
        Assert.That(dataTable.Rows[0][1], Is.EqualTo(string.Empty));
    }

    [Test]
    public void Read_MissingValue_MatchWithNullValue()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a;b;c\r\na;b\r\na;b;c"));
        var profile = new CsvProfile(';', '"', '\\', "\r\n", false, true, 512, string.Empty, "(null)");
        var reader = new CsvReader(profile);
        var dataTable = reader.ToDataTable(stream);
        Assert.That(dataTable.Rows[1][2], Is.EqualTo("(null)"));
    }

    [Test]
    [TestCase("a;b;c\r\n1;2;3")]
    [TestCase("a;b;c\r\n1;2;3\r\n")]
    [TestCase("a;b;c\r\n#\r\n1;2;3")]
    [TestCase("a;b;c\r\n#x;y;z\r\n1;2;3")]
    [TestCase("a;b;c\r\n1;2;3\r\n#x;y;z")]
    [TestCase("#x;y;z\r\na;b;c\r\n1;2;3")]
    [TestCase("#x;y;z\r\n#x;y;z\r\na;b;c\r\n1;2;3")]
    [TestCase("#x;y;z\r\n#x;y;z\r\na;b;c\r\n1;2;3\r\n#1;2;3")]
    public void Read_Comment_CommentedLinesSkipped(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var profile = new CsvProfile(new DialectDescriptor { Header = false, Delimiter = ';', CommentChar = '#', DoubleQuote = false });
        var reader = new CsvReader(profile);
        var dataTable = reader.ToDataTable(stream);
        Assert.That(dataTable.Rows.Count, Is.EqualTo(2));
        Assert.That(dataTable.Columns.Count, Is.EqualTo(3));
    }
}
