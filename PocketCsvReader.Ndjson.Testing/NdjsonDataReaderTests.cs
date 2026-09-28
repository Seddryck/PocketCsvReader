using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;
public class NdjsonDataReaderTests
{
    [Test]
    public void Read_FirstRowByIndex_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("clone"));
        Assert.That(dataReader.GetString(2), Is.EqualTo("src/clone_repo.ts"));
        Assert.That(dataReader.GetString(3), Is.EqualTo("335"));
    }

    [Test]
    public void Read_FirstRowByIndexTyped_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetFieldValue<string>(0), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetInt32(3), Is.EqualTo(335));
        Assert.That(dataReader.GetFieldValue<int>(3), Is.EqualTo(335));
    }

    [Test]
    public void GetOrdinal_ExistingNames_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetOrdinal("metric"), Is.EqualTo(0));
        Assert.That(dataReader.GetOrdinal("line"), Is.EqualTo(3));
    }

    [Test]
    public void GetName_BeforeGetOrdinal_ReturnsDecodedNames()
    {
        const string content = "{\"first\":1,\"na\\u006De\":2}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetName(0), Is.EqualTo("first"));
        Assert.That(reader.GetName(1), Is.EqualTo("name"));
        Assert.That(reader.GetOrdinal(reader.GetName(0)), Is.Zero);
        Assert.That(reader.GetOrdinal(reader.GetName(1)), Is.EqualTo(1));
    }

    [Test]
    public void GetName_HeterogeneousRecords_RefreshesCurrentNames()
    {
        const string content = "{\"first\":1,\"second\":2}\n{\"second\":3}\n{\"third\":4,\"first\":5}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, new NdjsonProfile("\n"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName),
            Is.EqualTo(new[] { "first", "second" }));

        Assert.That(reader.Read(), Is.True);
        Assert.That(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName),
            Is.EqualTo(new[] { "second" }));

        Assert.That(reader.Read(), Is.True);
        Assert.That(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName),
            Is.EqualTo(new[] { "third", "first" }));
    }

    [Test]
    public void GetName_InvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"value\":1}"));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetName(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetName(1));
    }

    [Test]
    public void GetName_NonObjectRoots_ReturnsEmptyName()
    {
        const string content = "[1,2]\n42\n\"Ada\"\n{}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, new NdjsonProfile("\n"));

        for (var row = 0; row < 3; row++)
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.FieldCount, Is.EqualTo(1));
            Assert.That(reader.GetName(0), Is.Empty);
            Assert.That(reader.GetOrdinal(string.Empty), Is.Zero);
        }

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetName(0));
    }

    [Test]
    public void GetOrdinal_ChangingNames_Success()
    {
        var content = "{\"foo\":123,\"bar\":true}\r\n{\"bar\":true}\r\n{\"bar\":true,\"foo\":123}\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetOrdinal("foo"), Is.EqualTo(0));
        Assert.That(dataReader.GetOrdinal("bar"), Is.EqualTo(1));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => dataReader.GetOrdinal("foo"));
        Assert.That(dataReader.GetOrdinal("bar"), Is.EqualTo(0));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetOrdinal("foo"), Is.EqualTo(1));
        Assert.That(dataReader.GetOrdinal("bar"), Is.EqualTo(0));
    }

    [Test]
    public void Read_FirstRowByNameTyped_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetFieldValue<string>("metric"), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetFieldValue<int>("line"), Is.EqualTo(335));
    }

    [Test]
    public void Read_FirstRowByNameWithSchema_Success()
    {
        var schema = new SchemaDescriptorBuilder().Named()
                            .WithField<string>("metric")
                            .WithIntegerField<int>("line")
                            .Build();
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, new NdjsonProfile(new NdjsonDialectDescriptor(), schema));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValue("metric"), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetValue("line"), Is.EqualTo(335));
    }

    [Test]
    public void Read_AllRows_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        for (int i = 0; i < 7; i++)
            Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void Read_NullValues_UsesDatabaseNullSemantics()
    {
        const string content = "{\"actual\":null,\"text\":\"null\",\"items\":[null,\"null\",1]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.GetValue(0), Is.SameAs(DBNull.Value));
        Assert.That(reader.GetFieldValue<string>(0), Is.Null);
        Assert.That(reader.GetFieldValue<int?>(0), Is.Null);
        Assert.Throws<InvalidCastException>(() => reader.GetString(0));

        Assert.That(reader.IsDBNull(1), Is.False);
        Assert.That(reader.GetString(1), Is.EqualTo("null"));
        Assert.That(reader.GetArray<string>(2), Is.EqualTo(new string?[] { null, "null", "1" }));
        Assert.That(reader.GetArray(2), Is.EqualTo(new object?[] { null, "null", "1" }));

        var values = new object[3];
        Assert.That(reader.GetValues(values), Is.EqualTo(3));
        Assert.That(values[0], Is.SameAs(DBNull.Value));
        Assert.That(values[1], Is.EqualTo("null"));
    }

    [Test]
    public void Read_NullRoot_IsDatabaseNull()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.GetValue(0), Is.SameAs(DBNull.Value));
    }

    [TestCase("\"quote: \\\"\"", "quote: \"")]
    [TestCase("\"backslash: \\\\\"", "backslash: \\")]
    [TestCase("\"solidus: \\/\"", "solidus: /")]
    [TestCase("\"controls: \\b\\f\\n\\r\\t\"", "controls: \b\f\n\r\t")]
    [TestCase("\"unicode: \\u00E9\"", "unicode: é")]
    [TestCase("\"pair: \\uD83D\\uDE00\"", "pair: 😀")]
    public void Read_EscapedRootString_ReturnsDecodedValue(string content, string expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(expected));
        Assert.That(reader.GetRawString(0), Is.EqualTo(content));
    }

    [Test]
    public void Read_EscapedPropertyAndArrayStrings_ReturnsDecodedValues()
    {
        const string content = "{\"na\\u006De\":[\"line\\nfeed\",\"\\u0041\"]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("name"), Is.Zero);
        Assert.That(reader.GetArray<string>(0), Is.EqualTo(new[] { "line\nfeed", "A" }));
    }

    [Test]
    public void GetRawString_CompositeFields_PreservesDelimitersAndWhitespace()
    {
        const string content = "{\"object\":{ \"name\": \"Ada\" },\"array\":[1, 2],\"emptyObject\":{},\"emptyArray\":[]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetRawString(0), Is.EqualTo("{ \"name\": \"Ada\" }"));
        Assert.That(reader.GetRawString(1), Is.EqualTo("[1, 2]"));
        Assert.That(reader.GetRawString(2), Is.EqualTo("{}"));
        Assert.That(reader.GetRawString(3), Is.EqualTo("[]"));
    }

    [Test]
    public void GetArray_CompositeElements_ReturnsStandaloneJsonValues()
    {
        const string content = "{\"items\":[{\"id\":1},[2,3],{},[],null,\"line\\nfeed\"]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray(0), Is.EqualTo(new object?[]
        {
            "{\"id\":1}",
            "[2,3]",
            "{}",
            "[]",
            null,
            "line\nfeed"
        }));
        Assert.That(reader.GetRawString(0), Is.EqualTo("[{\"id\":1},[2,3],{},[],null,\"line\\nfeed\"]"));
    }

    [TestCase("true")]
    [TestCase("false")]
    [TestCase("0")]
    [TestCase("-0")]
    [TestCase("123")]
    [TestCase("-12.34")]
    [TestCase("1e10")]
    [TestCase("1E-10")]
    [TestCase("1.2e+3")]
    public void Read_ValidLiteralOrNumber_PreservesValue(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetRawString(0), Is.EqualTo(content));
    }

    [TestCase("tru")]
    [TestCase("falsee")]
    [TestCase("nul")]
    [TestCase("undefined")]
    [TestCase("+1")]
    [TestCase("01")]
    [TestCase("-")]
    [TestCase("1.")]
    [TestCase(".1")]
    [TestCase("1e")]
    [TestCase("1e+")]
    [TestCase("--1")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("{\"value\":}")]
    [TestCase("{\"value\":1")]
    [TestCase("{\"value\":1} trailing")]
    [TestCase("{\"value\":1,}")]
    [TestCase("[1,]")]
    [TestCase("[1,2")]
    public void Read_InvalidJson_ThrowsInvalidDataException(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [TestCase("\"\\x\"")]
    [TestCase("\"\\u12\"")]
    [TestCase("\"\\uZZZZ\"")]
    [TestCase("\"\\uD83D\"")]
    [TestCase("\"\\uDE00\"")]
    [TestCase("\"\\uD83D\\u0041\"")]
    [TestCase("\"unterminated")]
    [TestCase("\"line\nbreak\"")]
    [TestCase("\"control \u0001\"")]
    public void Read_InvalidString_ThrowsInvalidDataException(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonDataReader(stream, NdjsonProfile.Default);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }
}
