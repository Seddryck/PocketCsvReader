﻿using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            public new string? RemoveTextQualifier(string item, char textQualifier, char escapeTextQualifier)
                => base.RemoveTextQualifier(item, textQualifier, escapeTextQualifier);

            public new IEnumerable<string?> SplitLine(string row, char fieldSeparator, char textQualifier, char escapeTextQualifier, string emptyCell)
                => base.SplitLine(row, fieldSeparator, textQualifier, escapeTextQualifier, emptyCell);

            public new int CountRecordSeparators(StreamReader reader, string recordSeparator, char commentChar, int bufferSize)
                => base.CountRecordSeparators(reader, recordSeparator, commentChar, bufferSize);

            public new string GetFirstRecord(StreamReader reader, string recordSeparator, int bufferSize)
                => base.GetFirstRecord(reader, recordSeparator, bufferSize);

            public new (IEnumerable<string>, string, bool) GetNextRecords(StreamReader reader, string recordSeparator, char commentChar, int bufferSize, string alreadyRead)
                => base.GetNextRecords(reader, recordSeparator, commentChar, bufferSize, alreadyRead);

            public new int IdentifyPartialRecordSeparator(string text, string recordSeparator)
                => base.IdentifyPartialRecordSeparator(text, recordSeparator);

            public new string CleanRecord(string record, string recordSeparator)
                => base.CleanRecord(record, recordSeparator);

        }

        [Test]
        [TestCase(null, "")]
        [TestCase("(null)", null)] //Parse (null) to a real null value
        [TestCase("\"(null)\"", "(null)")] //Explicitly quoted (null) should be (null)
        [TestCase("null", "null")]
        [TestCase("", "")]
        [TestCase("a", "a")]
        [TestCase("\"", "\"")]
        [TestCase("\"a", "\"a")]
        [TestCase("ab", "ab")]
        [TestCase("\"ab\"", "ab")]
        [TestCase("abc", "abc")]
        [TestCase("\"abc\"", "abc")]
        [TestCase("\"a\"\"b\"", "a\"b")]
        [TestCase("\"\"\"a\"\"b\"\"\"", "\"a\"b\"")]
        public void RemoveTextQualifier_String_CorrectString(string item, string result)
        {
            var reader = new CsvReaderProxy();
            var value = reader.RemoveTextQualifier(item, '\"', '\"');
            Assert.That(value, Is.EqualTo(result));
        }

        [Test]
        [TestCase("'a`'b'", "a'b")]
        [TestCase("'`'a`'b`''", "'a'b'")]
        public void RemoveTextQualifierWithEscaped_String_CorrectString(string item, string result)
        {
            var reader = new CsvReaderProxy();
            var value = reader.RemoveTextQualifier(item, '\'', '`');
            Assert.That(value, Is.EqualTo(result));
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
        public void SplitLine_RecordWithTwoFields_CorrectParsing(string record, string firstToken)
        {
            var reader = new CsvReaderProxy();
            var values = reader.SplitLine(record, ';', '\'', '\'', string.Empty).ToList();
            Assert.That(values[0], Is.EqualTo(firstToken));
            Assert.That(values[1], Is.EqualTo("xyz"));
        }

        [Test]
        [TestCase("'ab'';'c';'xyz'")]
        [TestCase("'ab'';'c''';'xyz'")]
        [TestCase("'ab'';'''c';'xyz'")]
        public void SplitLine_RecordWithUnescapedTextQualifier_ThrowException(string record)
        {
            var reader = new CsvReaderProxy();
            Assert.Throws<ArgumentException>(() => reader.SplitLine(record, ';', '\'', '\'', string.Empty).ToList());
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
        [TestCase(";;;", "")]
        public void SplitLine_RecordWithThreeFields_CorrectParsing(string record, string thirdToken)
        {
            var reader = new CsvReaderProxy();
            var values = reader.SplitLine(record, ';', '\'', '\'', string.Empty).ToList();
            Assert.That(values[2], Is.EqualTo(thirdToken));
        }

        [Test]
        public void SplitLine_NullField_NullValue()
        {
            var reader = new CsvReaderProxy();
            var values = reader.SplitLine("a;(null)", ';', char.MinValue, char.MinValue, string.Empty);
            Assert.That(values.ElementAt(1), Is.Null);
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

                var reader = new CsvReaderProxy();
                using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var value = reader.CountRecordSeparators(streamReader, recordSeparator, '#',  bufferSize);
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
        public void NextRecords_Csv_CorrectResults(string text, string recordSeparator, int bufferSize)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(text);
                writer.Flush();

                stream.Position = 0;

                var reader = new CsvReaderProxy();
                using (var streamReader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var (values, extraRead, eof) = reader.GetNextRecords(streamReader, recordSeparator, '#', bufferSize, string.Empty);
                    foreach (var value in values)
                    {
                        Assert.That(value, Does.StartWith("abc"));
                        Assert.That(value, Does.EndWith("abc").Or.EndWith("\0").Or.EndWith(recordSeparator));
                    }
                }
                writer.Dispose();
            }
        }

        [Test]
        [TestCase("a+b+c#a+b#a#a+b", '+', "#", "?")]
        public void NextRecords_CsvWithCsvProfileMissingCell_CorrectResults(string text, char fieldSeparator, string recordSeparator, string missingCell)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(text);
                writer.Flush();

                stream.Position = 0;
                var reader = new CsvReader();
                var dataTable = reader.Read(stream, Encoding.UTF8, 0, false, recordSeparator, fieldSeparator, '\"', '\"', '#', "_", missingCell);

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
        [TestCase("a+b+c#a++c#+b+c#+b+", '+', "#", "?")]
        public void NextRecords_CsvWithCsvProfileEmptyCell_CorrectResults(string text, char fieldSeparator, string recordSeparator, string emptyCell)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new StreamWriter(stream);
                writer.Write(text);
                writer.Flush();

                stream.Position = 0;
                var reader = new CsvReader();
                var dataTable = reader.Read(stream, Encoding.UTF8, 0, false, recordSeparator, fieldSeparator, '\"', '\"', '#', emptyCell, "_");

                Assert.That(dataTable.Rows[0].ItemArray[0], Is.EqualTo("a"));
                Assert.That(dataTable.Rows[0].ItemArray[1], Is.EqualTo("b"));
                Assert.That(dataTable.Rows[0].ItemArray[2], Is.EqualTo("c"));

                Assert.That(dataTable.Rows[1].ItemArray[0], Is.EqualTo("a"));
                Assert.That(dataTable.Rows[1].ItemArray[1], Is.EqualTo("?"));
                Assert.That(dataTable.Rows[1].ItemArray[2], Is.EqualTo("c"));

                Assert.That(dataTable.Rows[2].ItemArray[0], Is.EqualTo("?"));
                Assert.That(dataTable.Rows[2].ItemArray[1], Is.EqualTo("b"));
                Assert.That(dataTable.Rows[2].ItemArray[2], Is.EqualTo("c"));

                Assert.That(dataTable.Rows[3].ItemArray[0], Is.EqualTo("?"));
                Assert.That(dataTable.Rows[3].ItemArray[1], Is.EqualTo("b"));
                Assert.That(dataTable.Rows[3].ItemArray[2], Is.EqualTo("?"));

                writer.Dispose();
            }
        }

        [Test]
        [TestCase("abc", "+@", "abc")]
        [TestCase("abc+@", "+@", "abc")]
        [TestCase("abc\0\0\0", "+@", "abc")]
        [TestCase("", "+@", "")]
        public void CleanRecord_Record_CorrectResult(string text, string recordSeparator, string result)
        {
            var reader = new CsvReaderProxy();
            var value = reader.CleanRecord(text, recordSeparator);
            Assert.That(value, Is.EqualTo(result));
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
        public void Read_Csv_CorrectResult(string text, int bufferSize, int columnCount)
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
                Assert.That(dataTable.Columns, Has.Count.EqualTo(columnCount));
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
        [TestCase("abc", "123", 0)]
        [TestCase("abc1", "123", 1)]
        [TestCase("abc12", "123", 2)]
        [TestCase("abc12a", "123", 0)]
        [TestCase("", "123", 0)]
        [TestCase("", "#", 0)]
        [TestCase("abc", "#", 0)]
        public void IdentifyPartialRecordSeparator_Csv_CorrectResult(string text, string recordSeparator, int result)
        {
            var reader = new CsvReaderProxy(20);
            var value = reader.IdentifyPartialRecordSeparator(text, recordSeparator);
            Assert.That(value, Is.EqualTo(result));
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

                    var ex = Assert.Throws<InvalidDataException>(delegate { reader.Read(stream); });
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

                    var profile = new CsvProfile(new CsvDialectDescriptor { Header = false, Delimiter = ';', CommentChar='#', DoubleQuote=false });
                    var reader = new CsvReaderProxy(profile);
                    var dataTable = reader.Read(stream);
                    Assert.That(dataTable.Rows.Count, Is.EqualTo(2));
                    Assert.That(dataTable.Columns.Count, Is.EqualTo(3));
                }
            }
        }
    }
}
