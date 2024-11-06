using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace PocketCsvReader.Testing
{
    [TestFixture]
    public class InternalCsvReaderTest
    {
        class CsvReaderProxy : CsvReader
        {
            public CsvReaderProxy()
                : base(new CsvProfile(false)) { }

            public CsvReaderProxy(CsvProfile profile)
                : base(profile) { }

            public CsvReaderProxy(int bufferSize)
                : base(bufferSize) { }

            public new int CountRecordSeparators(StreamReader reader)
                => base.CountRecordSeparators(reader);

            public new string GetFirstRecord(StreamReader reader, string recordSeparator, int bufferSize)
                => base.GetFirstRecord(reader, recordSeparator, bufferSize);

            public new (string?[], bool) ReadNextRecord(StreamReader reader, Span<char> buffer, ref Span<char> extra)
                => base.ReadNextRecord(reader, buffer, ref extra);

            public new (string?[], bool) ReadNextRecord(Span<char> buffer)
                => base.ReadNextRecord(buffer);
        }

        [Test]
        [TestCase("foo", "foo")]
        public void ReadField_NotQualified_CorrectString(string item, string result)
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value = reader.ReadField(buffer, 0, item.Length, false, false);
            Assert.That(value, Is.EqualTo(result));
        }

        [Test]
        [TestCase("", "?")]
        public void ReadField_Empty_CorrectString(string item, string result)
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "?", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value = reader.ReadField(buffer, 0, item.Length, false, false);
            Assert.That(value, Is.EqualTo(result));
        }

        [Test]
        [TestCase("(null)", null)] //Parse (null) to a real null value
        [TestCase("\"(null)\"", "(null)")] //Explicitly quoted (null) should be (null)
        public void ReadField_Null_CorrectString(string item, string result)
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\"', '`', "\r\n", false, false, 4096, "?", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value = reader.ReadField(buffer, 0, item.Length, item.StartsWith("\""), item.StartsWith("\""));
            Assert.That(value, Is.EqualTo(result));
        }

        [TestCase("`a`", "a")]
        [TestCase("`foo`", "foo")]
        [TestCase("`foo bar`", "foo bar")]
        [TestCase("``", "?")]
        public void ReadField_Qualified_CorrectString(string item, string result)
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '`', '\\', "\r\n", false, false, 4096, "?", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value = reader.ReadField(buffer, 0, item.Length, true, true);
            Assert.That(value, Is.EqualTo(result));
        }

        [Test]
        [TestCase("'a`'b'", "a'b")]
        [TestCase("'`'a`'b`''", "'a'b'")]
        public void ReadField_EscapedWithOtherChar_CorrectString(string item, string result)
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value = reader.ReadField(buffer, 0, item.Length, true, true);
            Assert.That(value, Is.EqualTo(result));
        }

        [Test]
        [TestCase("\"")]
        [TestCase("\"a")]
        public void ReadField_ContainsQualifierChar_CorrectString(string item)
        {
            var profile = new CsvProfile(';', '\"', '\"', "\r\n", false, false, 4096, "(empty)", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value =
            Assert.Throws<InvalidDataException>(() =>
            {
                Span<char> buffer = stackalloc char[64];
                item.AsSpan().CopyTo(buffer);
                reader.ReadField(buffer, 0, item.Length, false, false);
            });
        }

        [TestCase("\"ab\"", "ab")]
        [TestCase("\"abc\"", "abc")]
        [TestCase("\"a\"\"b\"", "a\"b")]
        [TestCase("\"\"\"a\"\"b\"\"\"", "\"a\"b\"")]
        public void ReadField_EscapedWithDoubleChar_CorrectString(string item, string result)
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\"', '\"', "\r\n", false, false, 4096, "(empty)", "(null)");
            var reader = new CsvReaderProxy(profile);
            var value = reader.ReadField(buffer, 0, item.Length, true, true);
            Assert.That(value, Is.EqualTo(result));
        }

        [Test]
        [TestCase("foo")]
        [TestCase("'foo'")]
        [TestCase("foo;")]
        [TestCase("'foo';")]
        public void ReadNextRecord_SingleField_CorrectParsing(string record)
        {
            Span<char> buffer = stackalloc char[64];
            record.CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
            var reader = new CsvReaderProxy(profile);
            var (values, eof) = reader.ReadNextRecord(buffer);
            Assert.That(eof, Is.True);
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values.First(), Is.EqualTo("foo"));
        }

        [TestCase("foo\r\n", "foo")]
        [TestCase("foo;bar\r\n", "foo", "bar")]
        [TestCase("foo;bar;\r\n", "foo", "bar")]
        public void ReadNextRecord_RecordWithLineTerminator_CorrectParsing(string record, params string[] tokens)
        {
            Span<char> buffer = stackalloc char[64];
            record.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
            var reader = new CsvReaderProxy(profile);
            (var values, var _) = reader.ReadNextRecord(buffer);
            Assert.That(values, Has.Length.EqualTo(tokens.Length));
            for (int i = 0; i < tokens.Length; i++)
                Assert.That(values[i], Is.EqualTo(tokens[i]));
        }

        [TestCase("foo", "foo")]
        [TestCase("foo;bar", "foo", "bar")]
        [TestCase("foo;bar;", "foo", "bar")]
        public void ReadNextRecord_RecordWithoutLineTerminator_CorrectParsing(string record, params string[] tokens)
        {
            Span<char> buffer = stackalloc char[64];
            record.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
            var reader = new CsvReaderProxy(profile);
            (var values, var _) = reader.ReadNextRecord(buffer);
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
            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
            var reader = new CsvReaderProxy(profile);
            Assert.Throws<InvalidDataException>(() =>
            {
                Span<char> buffer = stackalloc char[64];
                record.CopyTo(buffer);
                reader.ReadNextRecord(buffer);
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
            Span<char> buffer = stackalloc char[64];
            record.AsSpan().CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "", "(null)");
            var reader = new CsvReaderProxy(profile);
            (var values, var _) = reader.ReadNextRecord(buffer);
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
            Span<char> buffer = stackalloc char[64];
            record.CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
            var reader = new CsvReaderProxy(profile);
            var (values, _) = reader.ReadNextRecord(buffer);
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values.First(), Is.EqualTo(expected));
        }

        [Test]
        [TestCase("'fo''o'", '\'')]
        [TestCase("'fo?'o'", '?')]
        [TestCase("'fo\\'o'", '\\')]
        public void ReadNextRecord_SingleFieldWithTextEscaper_CorrectParsing(string record, char escapeTextQualifier)
        {
            Span<char> buffer = stackalloc char[64];
            record.CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', escapeTextQualifier, "\r\n", false, true, 4096, string.Empty, string.Empty);
            var reader = new CsvReaderProxy(profile);
            var (values, _) = reader.ReadNextRecord(buffer);
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
            Span<char> buffer = stackalloc char[64];
            record.CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
            var reader = new CsvReaderProxy(profile);
            var (values, _) = reader.ReadNextRecord(buffer);
            Assert.That(values, Has.Length.EqualTo(3));
            Assert.That(values[2], Is.EqualTo(thirdToken));
        }

        [Test]
        public void ReadNextRecord_NullField_NullValue()
        {
            Span<char> buffer = stackalloc char[64];
            "a;(null)".CopyTo(buffer);

            var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
            var reader = new CsvReaderProxy(profile);
            var (values, eof) = reader.ReadNextRecord(buffer);
            Assert.That(eof, Is.True);
            Assert.That(values, Has.Length.EqualTo(2));
            Assert.That(values[1], Is.Null);
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
                var reader = new CsvReaderProxy(profile);
                using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true))
                {
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

                var reader = new CsvReaderProxy();
                using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true))
                {
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

                var reader = new CsvReaderProxy();
                using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var value = reader.GetFirstRecord(streamReader, recordSeparator, bufferSize);
                    Assert.That(value, Is.EqualTo("abc+abc" + recordSeparator).Or.EqualTo("abc+abc"));
                }
                writer.Dispose();
            }
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
            Span<char> buffer = stackalloc char[bufferSize];
            Span<char> extra = stackalloc char[0];

            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(text);
                writer.Flush();

                stream.Position = 0;

                var profile = new CsvProfile(';', recordSeparator);
                var reader = new CsvReaderProxy(profile);
                using (var streamReader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var (values, _) = reader.ReadNextRecord(streamReader, buffer, ref extra);
                    Assert.That(values, Has.Length.GreaterThan(0));
                    foreach (var value in values)
                        Assert.That(value, Is.EqualTo("abc"));
                }
                writer.Dispose();
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
                var dataTable = reader.Read(stream, Encoding.UTF8, 0);

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
                var dataTable = reader.Read(stream, Encoding.UTF8, 0);

                Assert.That(dataTable.Rows[0].ItemArray[0], Is.EqualTo("a"));
                Assert.That(dataTable.Rows[0].ItemArray[1], Is.EqualTo("b"));
                Assert.That(dataTable.Rows[0].ItemArray[2], Is.EqualTo("c"));

                for (int i = 0; i < 3;i++)
                    Assert.That(dataTable.Rows[1].ItemArray[i], Is.EqualTo(expected[i]));

                writer.Dispose();
            }
        }

        [Test]
        [TestCase("abc", "+@", "abc")]
        [TestCase("abc+@", "+@", "abc")]
        [TestCase("abc\0\0\0", "+@", "abc")]
        public void CleanRecord_Record_CorrectResult(string text, string recordSeparator, string result)
        {
            Span<char> buffer = stackalloc char[64];
            text.CopyTo(buffer);

            var profile = new CsvProfile(';', recordSeparator);
            var reader = new CsvReaderProxy(profile);
            var (value, _) = reader.ReadNextRecord(buffer);
            Assert.That(value[0], Is.EqualTo(result));
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
            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(text);
                writer.Flush();

                stream.Position = 0;

                var reader = new CsvReader(new CsvProfile(';', '\"', "\r\n", false, false, 4096, "(empty)", "(null)"), bufferSize);
                var dataTable = reader.Read(stream);
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
                writer.Dispose();
            }
        }

        [Test]
        [TestCase("'azerty';'';'alpha'", 3)]
        [TestCase("'azerty';;'alpha'", 3)]
        public void Read_CsvWithTextQualifier_CorrectResult(string text, int columnCount)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(text);
                writer.Flush();

                stream.Position = 0;

                var reader = new CsvReader(new CsvProfile(';', '\'', "\r\n", false, false, 4096, "foo", "(null)"));
                var dataTable = reader.Read(stream);
                Assert.That(dataTable.Columns, Has.Count.EqualTo(columnCount));
                Assert.That(dataTable.Rows[0][0], Is.EqualTo("azerty"));
                Assert.That(dataTable.Rows[0][1], Is.EqualTo("foo"));
                Assert.That(dataTable.Rows[0][2], Is.EqualTo("alpha"));
                writer.Dispose();
            }
        }

        [Test]
        [TestCase("a;b;c\r\nd;e;f;g\r\n", 1, 1)]
        [TestCase("a;b;c\r\nd;e;f\r\ng;h;i;j\r\n", 2, 1)]
        [TestCase("a;b;c\r\nd;e;f\r\ng;h;i;j;k\r\n", 2, 2)]
        public void Read_MoreFieldThanExpected_ExceptionMessage(string text, int rowNumber, int moreField)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(text);
                    writer.Flush();

                    stream.Position = 0;

                    var profile = CsvProfile.SemiColumnDoubleQuote;
                    var reader = new CsvReader(profile, 1024);

                    var ex = Assert.Throws<InvalidDataException>(() => reader.Read(stream));
                    Assert.That(ex!.Message, Does.Contain(string.Format("record {0} ", rowNumber + 1)));
                    Assert.That(ex.Message, Does.Contain(string.Format("{0} more", moreField)));
                }
            }
        }

        [Test]
        public void Read_EmptyValue_MatchWithEmpty()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write("a;;c");
                    writer.Flush();

                    stream.Position = 0;

                    var profile = CsvProfile.SemiColumnDoubleQuote;
                    var reader = new CsvReaderProxy(profile);
                    var dataTable = reader.Read(stream);
                    Assert.That(dataTable.Rows[0][1], Is.EqualTo(string.Empty));
                }
            }
        }

        [Test]
        public void Read_MissingValue_MatchWithNullValue()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write("a;b;c\r\na;b\r\na;b;c");
                    writer.Flush();

                    stream.Position = 0;

                    var profile = new CsvProfile(';', '"', "\r\n", false, true, 512, string.Empty, "(null)");
                    var reader = new CsvReaderProxy(profile);
                    var dataTable = reader.Read(stream);
                    Assert.That(dataTable.Rows[1][2], Is.EqualTo("(null)"));
                }
            }
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
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(content);
                    writer.Flush();

                    stream.Position = 0;

                    var profile = new CsvProfile(new CsvDialectDescriptor { Header = false, Delimiter = ';', CommentChar = '#', DoubleQuote = false });
                    var reader = new CsvReaderProxy(profile);
                    var dataTable = reader.Read(stream);
                    Assert.That(dataTable.Rows.Count, Is.EqualTo(2));
                    Assert.That(dataTable.Columns.Count, Is.EqualTo(3));
                }
            }
        }
    }
}
