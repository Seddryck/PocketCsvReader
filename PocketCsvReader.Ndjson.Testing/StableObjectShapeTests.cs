using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class StableObjectShapeTests
{
    [Test]
    public void Read_StableShape_ReusesFirstLabelsAndMapsLaterValuesByOrdinal()
    {
        using var reader = CreateReader(
            "{\"id\":1,\"name\":\"Ada\"}\n{\"renamed\":\"Grace\",\"other\":2}");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetName(0), Is.EqualTo("id"));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.EqualTo("id"));
            Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
            Assert.That(reader.GetInt32(1), Is.EqualTo(2));
        });
    }

    [Test]
    public void Read_StableShape_ReusesTopLevelFieldArrays()
    {
        using var reader = CreateReader(
            "{\"id\":1,\"payload\":{\"value\":2}}\n" +
            "{\"id\":3,\"payload\":{\"value\":4}}\n" +
            "{\"id\":5,\"payload\":{\"value\":6}}");

        Assert.That(reader.Read(), Is.True);
        var firstFields = GetCurrentFields(reader);

        Assert.That(reader.Read(), Is.True);
        var secondFields = GetCurrentFields(reader);
        Assert.That(secondFields, Is.Not.SameAs(firstFields));
        Assert.That(reader.GetInt32(0), Is.EqualTo(3));
        Assert.That(reader.GetRawString(1), Is.EqualTo("{\"value\":4}"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(GetCurrentFields(reader), Is.SameAs(firstFields));
        Assert.That(reader.GetInt32(0), Is.EqualTo(5));
    }

    [TestCase("{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2}")]
    [TestCase("{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2,\"name\":\"Grace\",\"active\":true}")]
    public void Read_PropertyCountChanges_ThrowsBeforeExposingRecord(string content)
    {
        using var reader = CreateReader(content);

        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
    }

    [Test]
    public async Task ReadAsync_PropertyCountChanges_ThrowsBeforeExposingRecord()
    {
        await using var reader = CreateReader("{\"id\":1}\n{\"id\":2,\"extra\":3}");

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(async () => await reader.ReadAsync(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Read_EmptyObjects_AreSupported()
    {
        using var reader = CreateReader("{}\n{}");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Read_EscapedLabelsNestedValuesAndComments_RetainBehavior()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithDialect(dialect => dialect.WithCommentChar('#'))
            .WithStableObjectShape()
            .Build();
        const string content =
            "{\"na\\u006de\":\"Ada\",\"payload\":{\"items\":[1,{\"deep\":true}]}} # first\n" +
            "{\"ignored\\u0020label\":\"Grace\",\"other\":{\"items\":[2,{\"deep\":false}]}} # second";
        using var reader = ndjson.ToDataReader(Stream(content));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("name"), Is.Zero);
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(reader.GetRawString(1), Does.Contain("deep"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
        Assert.That(reader.GetRawString(1), Does.Contain("false"));
    }

    [TestCase(
        "{\"payload\":{\"id\":1,\"name\":\"Ada\"}}\n{\"payload\":{\"id\":2}}")]
    [TestCase(
        "{\"payload\":{\"id\":1,\"name\":\"Ada\"}}\n{\"payload\":{\"id\":2,\"name\":\"Grace\",\"active\":true}}")]
    public void Read_NestedObjectShapeChanges_ThrowsBeforeExposingRecord(string content)
    {
        using var reader = CreateReader(content);

        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
        Assert.That(reader.GetRawString(0), Is.EqualTo("{\"id\":1,\"name\":\"Ada\"}"));
    }

    [Test]
    public void Read_NestedArrayLengthChanges_ThrowsBeforeExposingRecord()
    {
        using var reader = CreateReader(
            "{\"values\":[1,2]}\n{\"values\":[3,4,5]}");

        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 1, 2 }));
    }

    [TestCase("{\"value\":1}\n{\"value\":{\"nested\":2}}")]
    [TestCase("{\"value\":{\"nested\":1}}\n{\"value\":2}")]
    public void Read_ScalarContainerShapeChanges_ThrowsBeforeExposingRecord(string content)
    {
        using var reader = CreateReader(content);

        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Read_AcrossSmallBufferBoundaries_ReturnsAllValues()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 2, ReadAhead: false))
            .WithStableObjectShape()
            .Build();
        using var reader = ndjson.ToDataReader(Stream(
            "{\"identifier\":123,\"description\":\"first value\"}\n" +
            "{\"identifier\":456,\"description\":\"second value\"}"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(456));
            Assert.That(reader.GetString(1), Is.EqualTo("second value"));
        });
    }

    [Test]
    public void Read_WithProjection_UsesFirstObjectOrdinalsForLaterObjects()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithProjection(projection => projection.Property("name").Property("count"))
            .WithStableObjectShape()
            .Build();
        using var reader = ndjson.ToDataReader(Stream(
            "{\"ignored\":true,\"count\":1,\"name\":\"Ada\"}\n" +
            "{\"different\":false,\"alsoDifferent\":2,\"stillDifferent\":\"Grace\"}"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(2));
    }

    [Test]
    public void TypedAccessor_CachesConversionByOrdinal()
    {
        using var reader = CreateReader("{\"id\":1}\n{\"id\":2}");
        Assert.That(reader.Read(), Is.True);

        Assert.That(GetCachedConversionCount(reader), Is.Zero);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(GetCachedConversionCount(reader), Is.EqualTo(1));
        Assert.That(reader.GetValue(0), Is.EqualTo(1));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(GetCachedConversionCount(reader), Is.EqualTo(1));
    }

    [Test]
    public void SchemaDefinedField_CachesConversionByOrdinal()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithSchema(schema => schema.Named().WithIntegerField<int>("id"))
            .WithStableObjectShape()
            .Build();
        using var reader = ndjson.ToDataReader(Stream("{\"id\":1}\n{\"id\":2}"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(1));
        Assert.That(GetCachedConversionCount(reader), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(2));
        Assert.That(GetCachedConversionCount(reader), Is.EqualTo(1));
    }

    private static NdjsonDataReader CreateReader(string content)
        => new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithStableObjectShape()
            .Build()
            .ToDataReader(Stream(content));

    private static MemoryStream Stream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    private static int GetCachedConversionCount(NdjsonDataReader reader)
    {
        var parser = typeof(BaseDataRecord<NdjsonProfile>)
            .GetProperty("Parser", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(reader)!;
        var conversions = (System.Collections.IDictionary)parser.GetType()
            .GetProperty("FieldParsers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(parser)!;
        return conversions.Count;
    }

    private static FieldSpan[] GetCurrentFields(NdjsonDataReader reader)
        => (FieldSpan[])typeof(BaseRawRecord<NdjsonProfile>)
            .GetProperty("CurrentFieldSpans", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(reader)!;
}
