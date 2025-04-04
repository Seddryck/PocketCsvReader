using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class RecordParserTest
{
    [Test]
    [TestCase("foo")]
    [TestCase("'foo'")]
    [TestCase("foo\r\n")]
    [TestCase("'foo'\r\n")]
    public void ReadNextRecord_SingleField_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var eof = reader.IsEndOfFile(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(1));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("foo"));
    }

    [TestCase("foo\r\n", "foo")]
    [TestCase("foo;bar\r\n", "foo", "bar")]
    [TestCase("foo;bar;\r\n", "foo", "bar", "")]
    public void ReadNextRecord_RecordWithLineTerminator_CorrectParsing(string record, params string[] tokens)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(tokens.Length));
        for (int i = 0; i < tokens.Length; i++)
            Assert.That(values.Slice(i).ToString(), Is.EqualTo(tokens[i]));
    }

    [TestCase("foo", "foo")]
    [TestCase("foo;bar", "foo", "bar")]
    [TestCase("foo;bar;", "foo", "bar", "")]
    public void ReadNextRecord_RecordWithoutLineTerminator_CorrectParsing(string record, params string[] tokens)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(tokens.Length));
        for (int i = 0; i < tokens.Length; i++)
            Assert.That(values.Slice(i).ToString(), Is.EqualTo(tokens[i]));
    }

    [Test]
    [TestCase("'ab'';'c';'xyz'")]
    [TestCase("'ab'';'c''';'xyz'")]
    [TestCase("'ab'';'''c';'xyz'")]
    public void ReadNextRecord_RecordWithUnescapedTextQualifier_ThrowException(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));
        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        Assert.Throws<InvalidDataException>(() =>
        {
            reader.IsEndOfFile(out var values);
        });
    }

    [Test]
    [TestCase("abc;xyz", "abc")]
    [TestCase("'abc';'xyz'", "abc")]
    [TestCase("abc;'xyz'", "abc")]
    [TestCase("'abc';xyz", "abc")]
    [TestCase("'ab;c';xyz", "ab;c")]
    [TestCase("'ab;;c';xyz", "ab;;c")]
    [TestCase("'ab;;;c';xyz", "ab;;;c")]
    [TestCase("'a;b;;c';xyz", "a;b;;c")]
    [TestCase(";'xyz'", "")]
    [TestCase(";xyz", "")]
    public void ReadNextRecord_RecordWithTwoFields_CorrectParsing(string record, string firstToken)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true });
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values);
        Assert.That(values.Slice(0).ToString(), Is.EqualTo(firstToken));
        Assert.That(values.Slice(1).ToString(), Is.EqualTo("xyz"));
    }

    [Test]
    [TestCase("'fo;o'", "fo;o")]
    [TestCase("';foo'", ";foo")]
    [TestCase("'foo;'", "foo;")]
    public void ReadNextRecord_SingleFieldWithTextQualifier_CorrectParsing(string record, string expected)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(1));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo(expected));
    }


    [Test]
    [TestCase("abc;xyz;123", "123")]
    [TestCase("'abc';'xyz';'123'", "123")]
    [TestCase("abc;'xyz';123", "123")]
    [TestCase("'abc';xyz;123", "123")]
    [TestCase("'abc';xyz;'123'", "123")]
    [TestCase("'ab;;;c';xyz;", "")]
    [TestCase("'a;b;;c';'x;;;y;;z';123", "123")]
    [TestCase(";'xyz';", "")]
    [TestCase(";;", "")]
    public void ReadNextRecord_RecordWithThreeFields_CorrectParsing(string record, string thirdToken)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(3));
        Assert.That(values.Slice(2).ToString(), Is.EqualTo(thirdToken));
    }


    [Test]
    [TestCase("abc+abc", "+", 1)]
    [TestCase("abc+abc", "+", 2)]
    [TestCase("abc+abc", "+", 200)]
    [TestCase("abc+@abc", "+@", 1)]
    [TestCase("abc+@abc", "+@", 2)]
    [TestCase("abc+@abc", "+@", 4)]
    [TestCase("abc+@abc", "+@", 5)]
    [TestCase("abc+@abc", "+@", 200)]
    [TestCase("abc;abc+@abc", "+@", 1)]
    [TestCase("abc;abc+@abc", "+@", 2)]
    [TestCase("abc;abc+@abc", "+@", 3)]
    [TestCase("abc;abc+@abc", "+@", 4)]
    [TestCase("abc;abc+@abc", "+@", 5)]
    [TestCase("abc", "+@", 200)]
    public void ReadNextRecord_Csv_CorrectResults(string text, string recordSeparator, int bufferSize)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();

            stream.Position = 0;

            var profile = new CsvProfile(';', recordSeparator);
            using var reader = new RecordParser(new StreamReader(stream), profile, ArrayPool<char>.Create(256, 5));
            using (var streamReader = new StreamReader(stream, Encoding.UTF8, true))
            {
                reader.IsEndOfFile(out var values);
                Assert.That(values.FieldSpans, Has.Length.GreaterThan(0));
                for (int i = 0; i < values.FieldSpans.Length; i++)
                    Assert.That(values.Slice(i).ToString(), Is.EqualTo("abc"));
            }
            writer.Dispose();
        }
    }

    [TestCase("foo;bar")]
    [TestCase(" foo;bar")]
    [TestCase("         foo;bar")]
    [TestCase(" foo; bar")]
    [TestCase("foo;      bar")]
    [TestCase("`foo`;     `bar`")]
    public void ReadNextRecord_SkipInitialWhitespace_CorrectResults(string record)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var dialect = new DialectDescriptor() with { Delimiter = ';', QuoteChar = '`', SkipInitialSpace = true };
        var profile = new CsvProfile(dialect);
        using var reader = new RecordParser(new StreamReader(stream), profile, ArrayPool<char>.Create(256, 5));
        using var streamReader = new StreamReader(stream);
        reader.IsEndOfFile(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(2));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("foo"));
        Assert.That(values.Slice(1).ToString(), Is.EqualTo("bar"));
    }

    [Test]
    [TestCase("foo;bar\r\n1;2\r\n3;4", true, "foo", "bar")]
    [TestCase("foo;\r\n1;2\r\n3;4", true, "foo", "")]
    [TestCase("foo\r\n1;2\r\n3;4", true, "foo")]
    [TestCase("1;2\r\n3;4", false)]
    public void ReadHeaders_Record_CorrectResult(string text, bool hasHeader, params string[] headers)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var profile = new CsvProfile(';', '`', "\r\n", hasHeader);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var values = reader.ReadHeaders();
        Assert.That(values, Has.Length.EqualTo(Convert.ToInt32(hasHeader)));
        if (hasHeader)
        {
            Assert.That(values[0], Has.Length.EqualTo(headers.Length));
            for (int i = 0; i < headers.Length; i++)
                Assert.That(values[0][i], Is.EqualTo(headers[i]));
        }
    }

    [Test]
    [TestCase("abc+abc+abc+abc", "+", 1, 4)]
    [TestCase("abc+abc+abc+abc", "+", 2, 4)]
    [TestCase("abc+abc+abc+abc", "+", 200, 4)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 1, 4)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 2, 4)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 4, 4)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 5, 4)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 200, 4)]
    [TestCase("abc+@abc+abc+@abc", "+@", 1, 3)]
    [TestCase("abc+@abc+abc+@abc", "+@", 2, 3)]
    [TestCase("abc+@abc+abc+@abc", "+@", 4, 3)]
    [TestCase("abc+@abc+abc+@abc", "+@", 5, 3)]
    [TestCase("abc+@abc+abc+@abc", "+@", 200, 3)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 1, 3)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 2, 3)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 4, 3)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 5, 3)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 200, 3)]
    [TestCase("abc", "+@", 200, 1)]
    public void CountRecords_Csv_CorrectCount(string text, string recordSeparator, int bufferSize, int result)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var profile = new CsvProfile(';', recordSeparator)
        {
            ParserOptimizations = new ParserOptimizationOptions { RowCountAtStart = true }
        };

        using var streamReader = new StreamReader(stream, Encoding.UTF8, true);
        using var reader = new RecordParser(streamReader, profile, ArrayPool<char>.Create(256, 5));
        var value = reader.CountRecords();
        Assert.That(value, Is.EqualTo(result));
    }

    [TestCase("foo;bar\r\nfoo;bar\r\n", "\r\n", 4, 2)]
    [TestCase("foo;bar\r\nfoo;bar", "\r\n", 4, 2)]
    public void CountRecords_Rewind_CorrectCount(string text, string recordSeparator, int bufferSize, int result)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var profile = new CsvProfile(';', recordSeparator)
        {
            ParserOptimizations = new ParserOptimizationOptions { RowCountAtStart = true }
        };

        using var streamReader = new StreamReader(stream);
        using var reader = new RecordParser(streamReader, profile, ArrayPool<char>.Create(256, 5));
        var value = reader.CountRecords();
        Assert.That(value, Is.EqualTo(result));
        for (int i = 0; i < result; i++)
        {
            if (i < result - 1)
                Assert.That(reader.IsEndOfFile(out var _), Is.False);
            else
            {
                var eof = reader.IsEndOfFile(out var values);
                if (values.FieldSpans.Length == 0)
                    Assert.Pass();
                if (!eof)
                    Assert.That(reader.IsEndOfFile(out var _), Is.True);
            }
        }
    }

    [Test]
    [TestCase("abc+abc+abc+abc", "+", 1)]
    [TestCase("abc+abc+abc+abc", "+", 2)]
    [TestCase("abc+abc+abc+abc", "+", 200)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 1)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 2)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 4)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 5)]
    [TestCase("abc+@abc+@abc+@abc", "+@", 200)]
    [TestCase("abc+@abc+abc+@abc", "+@", 1)]
    [TestCase("abc+@abc+abc+@abc", "+@", 2)]
    [TestCase("abc+@abc+abc+@abc", "+@", 4)]
    [TestCase("abc+@abc+abc+@abc", "+@", 5)]
    [TestCase("abc+@abc+abc+@abc", "+@", 200)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 1)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 2)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 4)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 5)]
    [TestCase("abc+@abc+abc+@abc+@", "+@", 200)]
    [TestCase("abc", "+@", 200)]
    public void GetFirstRecord_Csv_CorrectResult(string text, string recordSeparator, int bufferSize)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();

            stream.Position = 0;


            using (var streamReader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var reader = new RecordParser(streamReader, new CsvProfile(',', '\"', '\\', recordSeparator, false, false, bufferSize, string.Empty, string.Empty), ArrayPool<char>.Create(256, 5));
                var value = reader.GetFirstRecord();
                Assert.That(value, Is.EqualTo("abc"));
            }
            writer.Dispose();
        }
    }

    [Test]
    [TestCase("abc+abc++abc+abc", "++", 1)]
    public void GetFirstRecord_CsvWithSemiSeparator_CorrectResult(string text, string recordSeparator, int bufferSize)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();

            stream.Position = 0;


            using (var streamReader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var reader = new RecordParser(streamReader, new CsvProfile(',', '\"', '\\', recordSeparator, false, false, bufferSize, string.Empty, string.Empty));
                var value = reader.GetFirstRecord();
                Assert.That(value, Is.EqualTo("abc+abc"));
            }
            writer.Dispose();
        }
    }


    [Test]
    public void ReadNextRecord_NullField_NullValue()
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes("a;(null)"));

        var profile = new CsvProfile(new DialectDescriptor() { Delimiter = ';', NullSequence = "(null)", Header = false });
        using var reader = new CsvDataReader(buffer, profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.False);
        Assert.That(reader.IsDBNull(1), Is.True);
    }
}
