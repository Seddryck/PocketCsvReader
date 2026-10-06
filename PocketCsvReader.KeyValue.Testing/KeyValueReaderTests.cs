using System.Text;
using NUnit.Framework;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class KeyValueReaderTests
{
    [Test]
    public async Task LtsvReader_ReadAsync_AdvancesAndExposesValues()
    {
        await using var reader = CreateReader(new LtsvReaderBuilder().Build(), "id:1\tname:Ada\nid:2\tname:Grace");

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Grace"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void LtsvReader_ReadsChangingFieldOrder()
    {
        const string content = "id:1\tname:Ada\nname:Grace\tid:2";
        using var reader = CreateReader(new LtsvReaderBuilder().Build(), content);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
            Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Ada"));
        });

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
            Assert.That(reader.GetName(0), Is.EqualTo("name"));
            Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(2));
            Assert.That(reader.Read(), Is.False);
        });
    }

    [Test]
    public void LogfmtReader_ReturnsDecodedAndRawValues()
    {
        const string content = "level=info message=\"hello \\\"Ada\\\"\" active";
        using var reader = CreateReader(new LogfmtReaderBuilder().Build(), content);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(reader.GetOrdinal("message")), Is.EqualTo("hello \"Ada\""));
            Assert.That(reader.GetRawString(reader.GetOrdinal("message")), Is.EqualTo("\"hello \\\"Ada\\\"\""));
            Assert.That(reader.GetString(reader.GetOrdinal("active")), Is.Empty);
        });
    }

    [Test]
    public void Reader_PreservesDuplicateLabelsAndReturnsFirstOrdinal()
    {
        using var reader = CreateReader(new LogfmtReaderBuilder().Build(), "tag=first tag=second");

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(2));
            Assert.That(reader.GetOrdinal("tag"), Is.Zero);
            Assert.That(reader.GetString(0), Is.EqualTo("first"));
            Assert.That(reader.GetString(1), Is.EqualTo("second"));
        });
    }

    [Test]
    public void TryGetOrdinal_WithLabeledRecord_ReturnsMatchOrNull()
    {
        using var reader = CreateReader(new LogfmtReaderBuilder().Build(), "level=info message=ready");
        Assert.That(reader.Read(), Is.True);

        var found = reader.TryGetOrdinal("message", out var matchingOrdinal);
        var missing = reader.TryGetOrdinal("timestamp", out var missingOrdinal);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(matchingOrdinal, Is.EqualTo(1));
            Assert.That(missing, Is.False);
            Assert.That(missingOrdinal, Is.Null);
        });
    }

    [TestCase("id:1\tname:Ada\r\nid:2\tname:Grace", KeyValueFormat.Ltsv)]
    [TestCase("id=1 name=Ada\nid=2 name=Grace", KeyValueFormat.Logfmt)]
    public void Reader_AcceptsStandardNewlinesAndFinalRecordWithoutNewline(string content, KeyValueFormat format)
    {
        var keyValueReader = format == KeyValueFormat.Ltsv
            ? new LtsvReaderBuilder().Build()
            : new LogfmtReaderBuilder().Build();
        using var reader = CreateReader(keyValueReader, content);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Grace"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Reader_ExposesEmptyRecord()
    {
        using var reader = CreateReader(new LtsvReaderBuilder().Build(), "\nid:1");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
    }

    [Test]
    public void Reader_ReadsRecordLargerThanItsConfiguredBuffer()
    {
        var expected = new string('x', 70_000);
        var keyValueReader = new LogfmtReaderBuilder()
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 128))
            .Build();
        using var reader = CreateReader(keyValueReader, $"message={expected}");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("message")), Is.EqualTo(expected));
    }

    [Test]
    public void ReaderBuilder_AppliesNamedSchemaAndParserOptimizations()
    {
        var builder = new LogfmtReaderBuilder()
            .WithSchema(schema => schema.Named())
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 128, ReadAhead: true, RowCountAtStart: true));
        var keyValueReader = builder.Build();
        using var reader = CreateReader(keyValueReader, "count=42");

        Assert.Multiple(() =>
        {
            Assert.That(builder, Is.TypeOf<LogfmtReaderBuilder>());
            Assert.That(keyValueReader.Format, Is.EqualTo(KeyValueFormat.Logfmt));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(reader.GetOrdinal("count")), Is.EqualTo(42));
        });
    }

    [Test]
    public void SharedMaterializers_ReturnExpectedValues()
    {
        const string content = "id:1\tname:Ada\nid:2\tname:Grace";
        var keyValueReader = new LtsvReaderBuilder().Build();

        var arrays = keyValueReader.ToArrayString(Stream(content)).ToArray();
        var table = keyValueReader.ToDataTable(Stream(content));

        Assert.Multiple(() =>
        {
            Assert.That(arrays, Is.EqualTo(new[]
            {
                new string?[] { "1", "Ada" },
                new string?[] { "2", "Grace" }
            }));
            Assert.That(table.Rows.Count, Is.EqualTo(2));
            Assert.That(table.Rows[1]["name"], Is.EqualTo("Grace"));
        });
    }

    private static KeyValueDataReader CreateReader(KeyValueReader reader, string content)
        => reader.ToDataReader(Stream(content));

    private static MemoryStream Stream(string content)
        => new(Encoding.UTF8.GetBytes(content));
}
