using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using PocketCsvReader.Configuration;
using Chrononuensis;
using Newtonsoft.Json.Linq;

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
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => dataReader.GetString(1));
        Assert.That(ex!.Message, Does.Contain("record '2'"));
        Assert.That(ex!.Message, Does.Contain("index '1'"));
        Assert.That(ex.Message, Does.Contain("contains 1 defined fields"));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));
    }

    [TestCase("a;xyz", "a")]
    [TestCase("'ab'';''c';'xyz'", "ab';'c")]
    [TestCase("'ab'';''''c';'xyz'", "ab';''c")]
    [TestCase("'a''b'';c';'xyz'", "a'b';c")]
    public void GetString_RecordWithTwoFields_CorrectParsing(string record, string firstToken)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
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
            new DialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
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
            new DialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
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
            new DialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetDateTime(1), Is.EqualTo(new DateTime(2024, 12, 06, 12, 45, 16)));
    }

    [Test]
    [TestCase("[10|25];foo", "10|25", "foo", 0)]
    [TestCase("foo;[10|25]", "foo", "10|25", 1)]
    public void GetArrayUntypedInt_RecordWithTwoFields_CorrectParsing(string content, string value1, string value2, int pos)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', ArrayDelimiter = '|', ArrayPrefix = '[', ArraySuffix = ']', Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.GetString(0), Is.EqualTo(value1));
        Assert.That(dataReader.GetString(1), Is.EqualTo(value2));
        var array = dataReader.GetArray(pos);
        Assert.That(array, Has.Length.EqualTo(2));
        Assert.That(array[0], Is.EqualTo("10"));
        Assert.That(array[1], Is.EqualTo("25"));
    }

    [Test]
    [TestCase("[10|25];foo", 0, 10, 25)]
    [TestCase("foo;[10|25|36]", 1, 10, 25, 36)]
    public void GetArrayInt_RecordWithTwoFields_CorrectParsing(string content, int pos, params int[] values)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', ArrayDelimiter = '|', ArrayPrefix = '[', ArraySuffix = ']', Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var array = dataReader.GetArray<int>(pos);
        Assert.That(array, Has.Length.EqualTo(values.Length));
        for (int i = 0; i < values.Length; i++)
            Assert.That(array[i], Is.EqualTo(values[i]));
    }

    [Test]
    [TestCase("[foo|bar];125", 0, "foo", "bar")]
    [TestCase("125;[foo|bar|qrz]", 1, "foo", "bar", "qrz")]
    public void GetArrayString_RecordWithTwoFields_CorrectParsing(string content, int pos, params string[] values)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', ArrayDelimiter = '|', ArrayPrefix = '[', ArraySuffix = ']', Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var array = dataReader.GetArray<string>(pos);
        Assert.That(array, Has.Length.EqualTo(values.Length));
        for (int i = 0; i < values.Length; i++)
            Assert.That(array[i], Is.EqualTo(values[i]));
    }


    [Test]
    [TestCase("[foo|bar];125", 0, "foo", "bar")]
    [TestCase("125;[foo|bar|qrz]", 1, "foo", "bar", "qrz")]
    public void GetArrayObject_RecordWithTwoFields_CorrectParsing(string content, int pos, params string[] values)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', ArrayDelimiter = '|', ArrayPrefix = '[', ArraySuffix = ']', Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var array = dataReader.GetArray(pos);
        Assert.That(array, Has.Length.EqualTo(values.Length));
        for (int i = 0; i < values.Length; i++)
            Assert.That(array[i], Is.EqualTo(values[i]));
    }

    [Test]
    [TestCase("[20-12-2024|20-04-2025];125", 0, "2024-12-20", "2025-04-20")]
    public void GetArrayUntypedDate_RecordWithTwoFields_CorrectParsing(string content, int pos, params string[] values)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithArray(array => array.WithDelimiter('|').WithPrefix('[').WithSuffix(']'))
                .WithoutHeader()
                .Build(),
            new SchemaDescriptorBuilder()
                .Indexed()
                .WithTemporalField<DateOnly>((f) => f.WithFormat("dd-MM-yyyy"))
                .Build()
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var array = dataReader.GetArray(pos);
        Assert.That(array, Has.Length.EqualTo(values.Length));
        for (int i = 0; i < values.Length; i++)
            Assert.That(array[i], Is.EqualTo(DateOnly.Parse(values[i])));
    }

    [Test]
    [TestCase("[2024-10|2025-04];125")]
    public void GetArrayCustomType_RecordWithTwoFields_CorrectParsing(string content)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithArray(array => array.WithDelimiter('|').WithPrefix('[').WithSuffix(']'))
                .WithoutHeader()
                .Build(),
            new SchemaDescriptorBuilder()
                .Indexed()
                .WithCustomField(typeof(YearMonth), (f) => f.WithFormat("yyyy-MM"))
                .Build()
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var array = dataReader.GetArray<YearMonth>(0);
        Assert.That(array, Has.Length.EqualTo(2));
        Assert.That(array[0], Is.EqualTo(new YearMonth(2024, 10)));
        Assert.That(array[1], Is.EqualTo(new YearMonth(2025, 4)));
    }

    #region ArrayItem
    [Test]
    [TestCase("[10|25];foo", 0, 1, 25)]
    [TestCase("foo;[10|25|36]", 1, 2, 36)]
    public void GetArrayItemInt_RecordWithTwoFields_CorrectParsing(string content, int pos, int item, int value)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', ArrayDelimiter = '|', ArrayPrefix = '[', ArraySuffix = ']', Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var result = dataReader.GetArrayItem<int>(pos, item);
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    [TestCase("[foo|bar];125", 0, 1, "bar")]
    [TestCase("125;[foo|bar|qrz]", 1, 2, "qrz")]
    public void GetArrayItemString_RecordWithTwoFields_CorrectParsing(string content, int pos, int item, string value)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptor() { Delimiter = ';', ArrayDelimiter = '|', ArrayPrefix = '[', ArraySuffix = ']', Header = false });
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var result = dataReader.GetArrayItem<string>(pos, item);
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    [TestCase("[20-12-2024|20-04-2025];125", 0, 0, "2024-12-20")]
    public void GetArrayItemDate_RecordWithTwoFields_CorrectParsing(string content, int pos, int item, string value)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithArray(array => array.WithDelimiter('|').WithPrefix('[').WithSuffix(']'))
                .WithoutHeader()
                .Build(),
            new SchemaDescriptorBuilder()
                .Indexed()
                .WithTemporalField<DateOnly>((f) => f.WithFormat("dd-MM-yyyy"))
                .Build()
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var result = dataReader.GetArrayItem<DateOnly>(pos, item);
        Assert.That(result, Is.EqualTo(DateOnly.Parse(value)));
    }

    [Test]
    [TestCase("[2024-10|2025-04];125")]
    public void GetArrayItemCustomType_RecordWithTwoFields_CorrectParsing(string content)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithArray(array => array.WithDelimiter('|').WithPrefix('[').WithSuffix(']'))
                .WithoutHeader()
                .Build(),
            new SchemaDescriptorBuilder()
                .Indexed()
                .WithCustomField(typeof(YearMonth), (f) => f.WithFormat("yyyy-MM"))
                .Build()
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        var result = dataReader.GetArrayItem<YearMonth>(0,1);
        Assert.That(result, Is.EqualTo(new YearMonth(2025,4)));
    }
    #endregion

    [Test]
    [TestCase("'fo\\'o'", '\\')]
    [TestCase("'fo?'o'", '?')]
    public void Read_SingleFieldWithTextEscaper_CorrectParsing(string record, char escapeTextQualifier)
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
    public void Read_SingleFieldWithDoubleQuote_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
                new DialectDescriptor() { Delimiter = ';', QuoteChar = '\'', DoubleQuote = true, Header = false }
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.That(dataReader.GetString(0), Is.EqualTo("fo'o"));
    }

    [Test]
    [TestCase("field0;field1\r\nfoo;bar")]
    public void Read_WithHeader_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
                new DialectDescriptor() { Delimiter = ';', Header = true }
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("field0"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("field1"));
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));
    }

    [Test]
    [TestCase("field;field\r\n0;1\r\nfoo;bar")]
    [TestCase("field\r\n0;1\r\nfoo;bar")]
    public void Read_WithHeaders_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
                new DialectDescriptor() { Delimiter = ';', HeaderRows = [1, 2], HeaderJoin = "." }
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("field.0"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("field.1"));
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));
    }

    [Test]
    [TestCase("field;field\r\n0;1\r\nfoo;bar")]
    [TestCase("field\r\n0;1\r\nfoo;bar")]
    public void Read_WithHeadersSkippingSomeRows_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(
                new DialectDescriptor() { Delimiter = ';', HeaderRows = [2], HeaderJoin = "." }
            );
        using var dataReader = new CsvDataReader(buffer, profile);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("0"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("1"));
        Assert.That(dataReader.GetString(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("bar"));
    }

    [Test]
    [TestCase("foo;bar\r\n2025/01/04;04-01-25")]
    public void Read_WithSchemaAndFormatDate_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithTemporalField<DateTime>("bar", (f) => f.WithFormat("dd-MM-yy"))
                    .WithTemporalField<DateTime>("foo", (f) => f.WithFormat(
                                                                    "yyyy/MM/dd"
                                                                    , (fmt) => fmt.WithDateSeparator("/")))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(DateTime)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(DateTime)));
        Assert.That(dataReader.GetDateTime(0), Is.EqualTo(new DateTime(2025, 1, 4)));
        Assert.That(dataReader.GetDateTime(1), Is.EqualTo(new DateTime(2025, 1, 4)));
        Assert.That(dataReader.GetDate(0), Is.EqualTo(new DateOnly(2025, 1, 4)));
        Assert.That(dataReader.GetDate(1), Is.EqualTo(new DateOnly(2025, 1, 4)));
    }

    [Test]
    [TestCase("foo;bar\r\n14:35:08;02:35:08 PM")]
    public void Read_WithSchemaAndFormatTime_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithTemporalField<TimeOnly>("foo", (f) => f.WithFormat("HH:mm:ss"))
                    .WithTemporalField<TimeOnly>("bar", (f) => f.WithFormat("hh:mm:ss tt"))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(TimeOnly)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(TimeOnly)));
        Assert.That(dataReader.GetTime(0), Is.EqualTo(new TimeOnly(14, 35, 8)));
        Assert.That(dataReader.GetTime(1), Is.EqualTo(new TimeOnly(14, 35, 8)));
    }

    [Test]
    [TestCase("foo;bar\r\n2025-01-04T14:35:08;01/04/2025 02:35:08 PM")]
    public void Read_WithSchemaAndFormatDateTime_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithTemporalField<DateTime>("foo", (f) => f.WithFormat("yyyy-MM-ddTHH:mm:ss"))
                    .WithTemporalField<DateTime>("bar", (f) => f.WithFormat("MM/dd/yyyy hh:mm:ss tt"))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(DateTime)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(DateTime)));
        Assert.That(dataReader.GetDateTime(0), Is.EqualTo(new DateTime(2025, 1, 4, 14, 35, 8)));
        Assert.That(dataReader.GetDateTime(1), Is.EqualTo(new DateTime(2025, 1, 4, 14, 35, 8)));
    }


    [Test]
    [TestCase("foo;bar\r\n2025-01-04T14:35:08Z;01/04/2025 02:35:08 PM +0100")]
    public void Read_WithSchemaAndFormatDateTimeOffset_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithTemporalField<DateTimeOffset>("foo", (f) => f.WithFormat("yyyy-MM-ddTHH:mm:ssZ"))
                    .WithTemporalField<DateTimeOffset>("bar", (f) => f.WithFormat("MM/dd/yyyy hh:mm:ss tt zzz"))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(DateTimeOffset)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(DateTimeOffset)));
        Assert.That(dataReader.GetDateTimeOffset(0), Is.EqualTo(new DateTimeOffset(2025, 1, 4, 14, 35, 8, new TimeSpan(0))));
        Assert.That(dataReader.GetDateTimeOffset(1), Is.EqualTo(new DateTimeOffset(2025, 1, 4, 14, 35, 8, new TimeSpan(1, 0, 0))));
    }

    [Test]
    [TestCase("foo;bar\r\n2025-01-04T14:35:08;108")]
    public void GetValue_WithSchemaAndFormatDateTimeAndShort_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithTemporalField<DateTime>("foo", (f) => f.WithFormat("yyyy-MM-ddTHH:mm:ss"))
                    .WithIntegerField<short>("bar")
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(DateTime)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(short)));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(new DateTime(2025, 1, 4, 14, 35, 8)));
        Assert.That(dataReader[0], Is.EqualTo(new DateTime(2025, 1, 4, 14, 35, 8)));
        Assert.That(dataReader.GetDate(0), Is.EqualTo(new DateOnly(2025, 1, 4)));
        Assert.That(dataReader.GetTime(0), Is.EqualTo(new TimeOnly(14, 35, 8)));
        Assert.That(dataReader.GetValue(1), Is.EqualTo((short)108));
        Assert.That(dataReader[1], Is.EqualTo((short)108));
        Assert.That(dataReader.GetInt32(1), Is.EqualTo(108));
        Assert.That(dataReader.GetInt64(1), Is.EqualTo((short)108));
        Assert.That(dataReader.GetFloat(1), Is.EqualTo((float)108));
        Assert.That(dataReader.GetDouble(1), Is.EqualTo((double)108));
        Assert.That(dataReader.GetDecimal(1), Is.EqualTo((decimal)108));
    }

    [Test]
    [TestCase("foo;bar\r\n2025-01-04T14:35:08;108")]
    public void GetFieldValue_WithSchemaAndFormatDateTimeAndShort_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithField<DateTime>("foo")
                    .WithField<short>("bar")
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(DateTime)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(short)));
        Assert.That(dataReader.GetFieldValue<DateTime>(0), Is.TypeOf<DateTime>());
        Assert.That(dataReader.GetFieldValue<short>(1), Is.TypeOf<short>());
        Assert.That(dataReader.GetFieldValue<int>(1), Is.TypeOf<int>());
        Assert.That(dataReader.GetFieldValue<decimal>(1), Is.TypeOf<decimal>());
        Assert.That(dataReader.GetFieldValue<DateTime>(0), Is.EqualTo(new DateTime(2025, 1, 4, 14, 35, 8)));
        Assert.That(dataReader.GetFieldValue<short>(1), Is.EqualTo((short)108));
        Assert.That(dataReader.GetFieldValue<int>(1), Is.EqualTo(108));
        Assert.That(dataReader.GetFieldValue<decimal>(1), Is.EqualTo(108m));
    }

    [Test]
    [TestCase("foo;bar\r\n125,210.17;456.211,205")]
    public void GetDecimal_WithDecimalCharAndGroupChar_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithNumberField<decimal>("foo", (f) => f.WithFormat((fmt) => fmt.WithDecimalChar('.').WithGroupChar(',')))
                    .WithNumberField<decimal>("bar", (f) => f.WithFormat((fmt) => fmt.WithDecimalChar(',').WithGroupChar('.')))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(decimal)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(decimal)));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(125210.17m));
        Assert.That(dataReader.GetValue(1), Is.EqualTo(456211.205m));
        Assert.That(dataReader.GetFieldValue<decimal>(0), Is.EqualTo(125210.17m));
        Assert.That(dataReader.GetFieldValue<decimal>(1), Is.EqualTo(456211.205m));
        Assert.That(dataReader.GetFloat(0), Is.EqualTo(125210.17m));
        Assert.That(dataReader.GetFloat(1), Is.EqualTo(456211.205m));
        Assert.That(dataReader.GetDouble(0), Is.EqualTo(125210.17m));
        Assert.That(dataReader.GetDouble(1), Is.EqualTo(456211.205m));
    }

    [Test]
    [TestCase("foo;bar\r\n+12,210;-16 211\r\n1e6;-456 211e2")]
    public void GetInt32_WithGroupChar_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithNumberField<int>("foo", (f) => f.WithFormat((fmt) => fmt.WithGroupChar(",")))
                    .WithNumberField<int>("bar", (f) => f.WithFormat((fmt) => fmt.WithGroupChar(" ")))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(int)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(int)));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(12210));
        Assert.That(dataReader.GetFieldValue<int>(0), Is.EqualTo(12210));
        Assert.That(dataReader.GetFieldValue<int>(1), Is.EqualTo(-16211));
        Assert.That(dataReader.GetValue(1), Is.EqualTo(-16211));
        Assert.That(dataReader.GetInt64(0), Is.EqualTo(12210));
        Assert.That(dataReader.GetInt64(1), Is.EqualTo(-16211));
        Assert.That(dataReader.GetInt16(0), Is.EqualTo(12210));
        Assert.That(dataReader.GetInt16(1), Is.EqualTo(-16211));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetFieldValue<int>(0), Is.EqualTo(1000000));
        Assert.That(dataReader.GetFieldValue<int>(1), Is.EqualTo(-45621100));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(1000000));
        Assert.That(dataReader.GetValue(1), Is.EqualTo(-45621100));
        Assert.That(dataReader.GetInt64(0), Is.EqualTo(1000000));
        Assert.That(dataReader.GetInt64(1), Is.EqualTo(-45621100));
        Assert.Throws<OverflowException>(() => dataReader.GetInt16(0));
        Assert.Throws<OverflowException>(() => dataReader.GetInt16(1));
    }

    [Test]
    [TestCase("foo\r\n+12,210")]
    public void GetInt32_WithoutGroupChar_ThrowsFormat(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithNumberField<int>("foo", (f) => f.WithFormat((fmt) => fmt.WithoutGroupChar()))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(int)));
        Assert.Throws<FormatException>(() => dataReader.GetFieldValue<int>(0));
        Assert.Throws<FormatException>(() => dataReader.GetValue(0));
        Assert.Throws<FormatException>(() => dataReader.GetInt32(0));
    }

    [Test]
    [TestCase("foo\r\n+12210")]
    public void GetInt32_WithoutGroupChar_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithNumberField<int>("foo", (f) => f.WithFormat((fmt) => fmt.WithoutGroupChar()))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(int)));
        Assert.That(dataReader.GetFieldValue<int>(0), Is.EqualTo(12210));
    }

    [Test]
    [TestCase("foo;bar\r\n2025-01-04T14:35:08;108")]
    public void GetFieldValue_WithoutSchema_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true)
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(object)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(object)));
        Assert.That(dataReader.GetFieldValue<DateTime>(0), Is.TypeOf<DateTime>());
        Assert.That(dataReader.GetFieldValue<short>(1), Is.TypeOf<short>());
        Assert.That(dataReader.GetFieldValue<int>(1), Is.TypeOf<int>());
        Assert.That(dataReader.GetFieldValue<decimal>(1), Is.TypeOf<decimal>());
        Assert.That(dataReader.GetFieldValue<DateTime>(0), Is.EqualTo(new DateTime(2025, 1, 4, 14, 35, 8)));
        Assert.That(dataReader.GetFieldValue<short>(1), Is.EqualTo((short)108));
        Assert.That(dataReader.GetFieldValue<int>(1), Is.EqualTo(108));
        Assert.That(dataReader.GetFieldValue<decimal>(1), Is.EqualTo(108m));
    }

    [Test]
    [TestCase("foo\r\nd3c7f3e0-4b3a-4a95-a6b9-81a519e4a8c1")]
    public void GetGuid_ValidGuid_CorrectParsing(string record)
    {
        var guid = new Guid("d3c7f3e0-4b3a-4a95-a6b9-81a519e4a8c1");
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithField<Guid>("foo")
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(Guid)));
        Assert.That(dataReader.GetValue(0), Is.TypeOf<Guid>());
        Assert.That(dataReader.GetFieldValue<Guid>(0), Is.EqualTo(guid));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(guid));
        Assert.That(dataReader[0], Is.EqualTo(guid));
        Assert.That(dataReader["foo"], Is.EqualTo(guid));
        Assert.That(dataReader.GetGuid(0), Is.EqualTo(guid));
    }

    [Test]
    [TestCase("foo;bar\r\n(null);NaN\r\n;")]
    public void Read_WithSchemaAndSequences_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true)
            )
            .WithSchema(
                (schema) => schema
                    .Indexed()
                    .WithField<string>((f) => f.WithSequence("(null)", null))
                    .WithField<string>((f) => f.WithSequence("NaN", null))

            )
            .WithResource((r) => r.WithSequence("", null));
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.IsDBNull(0), Is.True);
        Assert.That(dataReader.IsDBNull(1), Is.True);
        dataReader.Read();
        Assert.That(dataReader.IsDBNull(0), Is.True);
        Assert.That(dataReader.IsDBNull(1), Is.True);
        Assert.That(dataReader.GetFieldValue<string?>(1), Is.Null);
        Assert.That(dataReader.GetFieldValue<string>(1), Is.Null);
        Assert.Throws<InvalidCastException>(() => dataReader.GetValue(1));
        Assert.Throws<InvalidCastException>(() => dataReader.GetString(1));
    }

    [Test]
    [TestCase("foo;bar\r\n20;NaN\r\n;15")]
    public void Read_WithSchemaNotStringAndSequences_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true)
            )
            .WithSchema(
                (schema) => schema
                    .Indexed()
                    .WithField<int>()
                    .WithField<int>((f) => f.WithSequence("NaN", "0"))

            )
            .WithResource((r) => r.WithSequence("", "-1"));
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.IsDBNull(0), Is.False);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(20));
        Assert.That(dataReader.IsDBNull(1), Is.False);
        Assert.That(dataReader.GetInt32(1), Is.EqualTo(0));
        dataReader.Read();
        Assert.That(dataReader.IsDBNull(0), Is.False);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(-1));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(-1));
        Assert.That(dataReader.GetFieldValue<int>(0), Is.EqualTo(-1));
        Assert.That(dataReader.IsDBNull(1), Is.False);
        Assert.That(dataReader.GetInt32(1), Is.EqualTo(15));
    }

    [Test]
    [TestCase("foo;bar\r\n2025-02;Q1.25")]
    public void Read_WithSchemaParsableTypes_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var builder = new CsvReaderBuilder()
            .WithDialect(
                (dialect) => dialect
                    .WithDelimiter(';')
                    .WithHeader(true))
            .WithSchema(
                (schema) => schema
                    .Named()
                    .WithCustomField<Chrononuensis.YearMonth>("foo", (f) => f.WithFormat("yyyy-MM"))
                    .WithCustomField<Chrononuensis.YearQuarter>("bar", (f) => f.WithFormat("'Q'q.yy"))
            );
        using var dataReader = builder.Build().ToDataReader(buffer);
        dataReader.Read();
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetName(0), Is.EqualTo("foo"));
        Assert.That(dataReader.GetName(1), Is.EqualTo("bar"));
        Assert.That(dataReader.GetFieldType(0), Is.EqualTo(typeof(Chrononuensis.YearMonth)));
        Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(Chrononuensis.YearQuarter)));
        Assert.That(dataReader.GetValue(0), Is.EqualTo(new Chrononuensis.YearMonth(2025, 2)));
        Assert.That(dataReader.GetValue(1), Is.EqualTo(new Chrononuensis.YearQuarter(2025, 1)));

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
            while (dataReader.Read())
            {
                rowCount++;
                Assert.That(dataReader.FieldCount, Is.EqualTo(14));
            }
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
            Assert.Throws<ArgumentOutOfRangeException>(() => dataReader.GetOrdinal("foo"));
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
    //[TestCase(1, true)]
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
                Assert.That(dataReader.FieldCount, Is.EqualTo(25), $"Row {rowCount}: Expected 25 fields but got {dataReader.FieldCount}");
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
                Assert.That(dataReader.FieldCount, Is.EqualTo(25), $"Row {rowCount}: Expected 25 fields but got {dataReader.FieldCount}");
                for (var i = 0; i < dataReader.FieldCount; i++)
                    Assert.That(dataReader.GetString(i), Is.EqualTo(reference[rowCount - 1][i]), $"Row {rowCount}, record {i}: {dataReader.GetString(i)}");
            }
            Assert.That(rowCount, Is.EqualTo(lineCount));
        }
    }

    [Test]
    [TestCase("\r\n")]
    [TestCase("\n")]
    [TestCase("\t\r\n")]
    public void ToDataReader_ReadUntilEndOfFile(string lineTerminator)
    {
        var dialect = new DialectDescriptor()
        {
            Delimiter = ',',
            LineTerminator = lineTerminator,
            Header = true
        };
        var profile = new CsvProfile(dialect);
        var data = $"a,b,c{lineTerminator}1,2,3{lineTerminator}4,5,6{lineTerminator}";
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(data)), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    [TestCase("a;b;c;d\r\n125;foo;2025-12-25\r\n256;bar\r\n304;qrz;2025-04-01;10.25")]
    public void GetValues_MixedTypes_Correct(string input)
    {
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithLineTerminator("\r\n")
                .WithHeader(true)
                .Build(),
            new SchemaDescriptorBuilder()
                .Indexed()
                .WithField<int>()
                .WithField<string>()
                .WithField<DateOnly>()
                .Build());

        var values = new object[3];

        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var dataReader = new CsvDataReader(buffer, profile);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValues(values), Is.EqualTo(3));
        Assert.That(values[0], Is.EqualTo(125));
        Assert.That(values[1], Is.EqualTo("foo"));
        Assert.That(values[2], Is.EqualTo(new DateOnly(2025, 12, 25)));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValues(values), Is.EqualTo(2));
        Assert.That(values[0], Is.EqualTo(256));
        Assert.That(values[1], Is.EqualTo("bar"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValues(values), Is.EqualTo(3));
        Assert.That(values[0], Is.EqualTo(304));
        Assert.That(values[1], Is.EqualTo("qrz"));
        Assert.That(values[2], Is.EqualTo(new DateOnly(2025, 4, 1)));
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    [TestCase("a;b;c;d\r\n125;foo;2025-12-25\r\n256;bar\r\n304;qrz;2025-04-01;10.25")]
    public void GetValues_MixedUntyped_Correct(string input)
    {
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithLineTerminator("\r\n")
                .WithHeader(true)
                .Build());

        var values = new object[3];

        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var dataReader = new CsvDataReader(buffer, profile);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValues(values), Is.EqualTo(3));
        Assert.That(values[0], Is.EqualTo("125"));
        Assert.That(values[1], Is.EqualTo("foo"));
        Assert.That(values[2], Is.EqualTo("2025-12-25"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValues(values), Is.EqualTo(2));
        Assert.That(values[0], Is.EqualTo("256"));
        Assert.That(values[1], Is.EqualTo("bar"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValues(values), Is.EqualTo(3));
        Assert.That(values[0], Is.EqualTo("304"));
        Assert.That(values[1], Is.EqualTo("qrz"));
        Assert.That(values[2], Is.EqualTo("2025-04-01"));
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    [TestCase("a;b;c;d\r\n125;foo;2025-12-25\r\n256;bar\r\n304;qrz;2025-04-01;10.25")]
    public void GetValues_NullInput_Correct(string input)
    {
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(';')
                .WithLineTerminator("\r\n")
                .WithHeader(true)
                .Build());

        object[]? values = null;

        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var dataReader = new CsvDataReader(buffer, profile);
        Assert.That(dataReader.Read(), Is.True);
        Assert.Throws<ArgumentNullException>(() => dataReader.GetValues(values!));
    }
}
