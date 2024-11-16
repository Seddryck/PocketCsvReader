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
    [TestCase("foo;")]
    [TestCase("'foo';")]
    public void ReadNextRecord_SingleField_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var (values, eof) = reader.ReadNextRecord();
        Assert.That(eof, Is.True);
        Assert.That(values, Has.Length.EqualTo(1));
        Assert.That(values.First(), Is.EqualTo("foo"));
    }

    [TestCase("foo\r\n", "foo")]
    [TestCase("foo;bar\r\n", "foo", "bar")]
    [TestCase("foo;bar;\r\n", "foo", "bar")]
    public void ReadNextRecord_RecordWithLineTerminator_CorrectParsing(string record, params string[] tokens)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        (var values, var _) = reader.ReadNextRecord();
        Assert.That(values, Has.Length.EqualTo(tokens.Length));
        for (int i = 0; i < tokens.Length; i++)
            Assert.That(values[i], Is.EqualTo(tokens[i]));
    }

    [TestCase("foo", "foo")]
    [TestCase("foo;bar", "foo", "bar")]
    [TestCase("foo;bar;", "foo", "bar")]
    public void ReadNextRecord_RecordWithoutLineTerminator_CorrectParsing(string record, params string[] tokens)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        (var values, var _) = reader.ReadNextRecord();
        Assert.That(values, Has.Length.EqualTo(tokens.Length));
        for (int i = 0; i < tokens.Length; i++)
            Assert.That(values[i], Is.EqualTo(tokens[i]));
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
            reader.ReadNextRecord();
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
    [TestCase("'ab'';''c';'xyz'", "ab';'c")]
    [TestCase("'ab'';''''c';'xyz'", "ab';''c")]
    [TestCase("'a''b'';c';'xyz'", "a'b';c")]
    public void ReadNextRecord_RecordWithTwoFields_CorrectParsing(string record, string firstToken)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "", "(null)");
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        (var values, var _) = reader.ReadNextRecord();
        Assert.That(values[0], Is.EqualTo(firstToken));
        Assert.That(values[1], Is.EqualTo("xyz"));
    }

    [Test]
    [TestCase("'fo;o'", "fo;o")]
    [TestCase("'fo;o';", "fo;o")]
    [TestCase("';foo';", ";foo")]
    [TestCase("'foo;';", "foo;")]
    public void ReadNextRecord_SingleFieldWithTextQualifier_CorrectParsing(string record, string expected)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var (values, _) = reader.ReadNextRecord();
        Assert.That(values, Has.Length.EqualTo(1));
        Assert.That(values.First(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase("'fo''o'", '\'')]
    [TestCase("'fo?'o'", '?')]
    [TestCase("'fo\\'o'", '\\')]
    public void ReadNextRecord_SingleFieldWithTextEscaper_CorrectParsing(string record, char escapeTextQualifier)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '\'', escapeTextQualifier, "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var (values, _) = reader.ReadNextRecord();
        Assert.That(values, Has.Length.EqualTo(1));
        Assert.That(values.First(), Is.EqualTo("fo'o"));
    }

    [Test]
    [TestCase("abc;xyz;123", "123")]
    [TestCase("'abc';'xyz';'123'", "123")]
    [TestCase("abc;'xyz';123", "123")]
    [TestCase("'abc';xyz;123", "123")]
    [TestCase("'abc';xyz;'123'", "123")]
    [TestCase("'ab;;;c';xyz;;", "")]
    [TestCase("'a;b;;c';'x;;;y;;z';123", "123")]
    [TestCase(";'xyz';;", "")]
    [TestCase(";;;", "")]
    public void ReadNextRecord_RecordWithThreeFields_CorrectParsing(string record, string thirdToken)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var (values, _) = reader.ReadNextRecord();
        Assert.That(values, Has.Length.EqualTo(3));
        Assert.That(values[2], Is.EqualTo(thirdToken));
    }

    [Test]
    public void ReadNextRecord_NullField_NullValue()
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes("a;(null)\0"));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var (values, eof) = reader.ReadNextRecord();
        Assert.That(eof, Is.True);
        Assert.That(values, Has.Length.EqualTo(2));
        Assert.That(values[1], Is.Null);
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
                var (values, _) = reader.ReadNextRecord();
                Assert.That(values, Has.Length.GreaterThan(0));
                foreach (var value in values)
                    Assert.That(value, Is.EqualTo("abc"));
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
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(record + '\0'));

        var profile = new CsvProfile(';', '`');
        profile.Descriptor.SkipInitialSpace = true;
        using var reader = new RecordParser(new StreamReader(stream), profile, ArrayPool<char>.Create(256, 5));
        using var streamReader = new StreamReader(stream);
        var (values, _) = reader.ReadNextRecord();
        Assert.That(values, Has.Length.EqualTo(2));
        Assert.That(values[0], Is.EqualTo("foo"));
        Assert.That(values[1], Is.EqualTo("bar"));
    }

    [Test]
    [TestCase("abc\0", "+@", "abc")]
    [TestCase("abc+@\0", "+@", "abc")]
    [TestCase("abc\0\0\0", "+@", "abc")]
    public void CleanRecord_Record_CorrectResult(string text, string recordSeparator, string result)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var profile = new CsvProfile(';', recordSeparator);
        using var reader = new RecordParser(new StreamReader(buffer), profile, ArrayPool<char>.Create(256, 5));
        var (value, _) = reader.ReadNextRecord();
        Assert.That(value[0], Is.EqualTo(result));
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
    public void CountRecordSeparator_Csv_CorrectCount(string text, string recordSeparator, int bufferSize, int result)
    {
        using (var stream = new MemoryStream())
        {
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();

            stream.Position = 0;

            var profile = new CsvProfile(';', recordSeparator);

            using (var streamReader = new StreamReader(stream, Encoding.UTF8, true))
            {
                using var reader = new RecordParser(streamReader, profile, ArrayPool<char>.Create(256, 5));
                var value = reader.CountRecordSeparators(streamReader);
                Assert.That(value, Is.EqualTo(result));
            }
            writer.Dispose();
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
                var reader = new RecordParser(streamReader, CsvProfile.SemiColumnDoubleQuote, ArrayPool<char>.Create(256, 5));
                var value = reader.GetFirstRecord(streamReader, recordSeparator, bufferSize);
                Assert.That(value, Is.EqualTo("abc" + recordSeparator).Or.EqualTo("abc"));
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
                var reader = new RecordParser(streamReader, CsvProfile.SemiColumnDoubleQuote);
                var value = reader.GetFirstRecord(streamReader, recordSeparator, bufferSize);
                Assert.That(value, Is.EqualTo("abc+abc" + recordSeparator).Or.EqualTo("abc+abc"));
            }
            writer.Dispose();
        }
    }
}
