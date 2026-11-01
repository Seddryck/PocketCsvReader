using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Json.Testing;

[TestFixture(typeof(JsonProfile))]
[TestFixture(typeof(NdjsonProfile))]
public class LazyJsonDecodingTests<TProfile>
    where TProfile : IProfile
{
    [Test]
    public async Task ValuesDecodeOnAccessAndCachesReset(
        [Values(false, true)] bool async,
        [Values("Default", "Projection", "Ordered", "Path")] string mode)
    {
        using var reader = Open([
            """{"text":"first\nvalue","number":42,"items":["\u0041"]}""",
            """{"text":"second\tvalue","number":43,"items":["\u0042"]}""",
            """{"text":"third\u0020value","number":44,"items":["\u0043"]}"""
        ], mode);
        string[] expected = ["first\nvalue", "second\tvalue", "third value"];
        string? saved = null;
        for (int row = 0; row < expected.Length; row++)
        {
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            var fields = Fields(reader);
            Assert.That(fields[0].DecodedValue, Is.Null);
            Assert.That(fields[2].Children![0].DecodedValue, Is.Null);
            Assert.That(reader.IsDBNull(0), Is.False);
            _ = reader.GetRawString(0);
            Assert.That(fields[0].DecodedValue, Is.Null, "Null checks and raw getters must not decode.");
            Assert.That(reader.GetString(0), Is.EqualTo(expected[row]));
            Assert.That(fields[0].DecodedValue, Is.EqualTo(expected[row]));
            var decoded = fields[0].DecodedValue;
            Assert.That(reader.GetString(0), Is.SameAs(decoded));
            Assert.That(reader.GetArrayItem<string>(2, 0), Is.EqualTo(((char)('A' + row)).ToString()));
            Assert.That(fields[2].Children![0].DecodedValue, Is.Not.Null);
            if (row == 0) saved = decoded;
        }
        Assert.That(saved, Is.EqualTo(expected[0]), "Returned strings must outlive borrowed row storage.");
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    public static IEnumerable<string> InvalidEscapes =>
        ["\\q", "\\u12", "\\uZZZZ", "\\uD800", "\\uDC00", "\\uD800\\u0041", "\\uD800\\n"];

    [Test]
    public async Task UnaccessedEscapesAreValidatedDuringRead(
        [ValueSource(nameof(InvalidEscapes))] string escape,
        [Values(false, true)] bool async,
        [Values("Default", "Projection", "Ordered", "Path")] string mode,
        [Values(false, true)] bool nested)
    {
        string invalid = "\"" + escape + "\"";
        string unused = nested ? "{\"nested\":[" + invalid + "]}" : invalid;
        using var reader = Open(["{\"text\":\"ok\",\"number\":1,\"items\":[],\"unused\":" + unused + "}"], mode);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            if (async) await reader.ReadAsync(); else reader.Read();
        });
        Assert.That(error!.Message, Does.Match("(?i)escape|surrogate|hexadecimal"));
    }

    [Test]
    public void TypedArrayAndCustomGettersReceiveDecodedText(
        [Values("Default", "Projection", "Ordered", "Path")] string mode)
    {
        using var reader = Open(["""{"text":"line\nfeed","num\u0062er":"\u0034\u0032","items":["\u0041",null,"\u0042"]}"""], mode);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("number"), Is.EqualTo(1));
        Assert.That(reader.GetFieldValue<int>(1), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue(1, (ReadOnlySpan<char> value) => int.Parse(value)), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue(1, (string value) => int.Parse(value)), Is.EqualTo(42));
        Assert.That(reader.GetArray<string>(2), Is.EqualTo(new string?[] { "A", null, "B" }));
        Assert.That(reader.GetArray(2), Is.EqualTo(new object?[] { "A", null, "B" }));
        Assert.That(reader.GetValue(0), Is.EqualTo("line\nfeed"));
        object[] values = new object[3];
        Assert.That(reader.GetValues(values), Is.EqualTo(3));
        Assert.That(values[0], Is.EqualTo("line\nfeed"));
        Assert.That(reader.GetRawString(1), Is.EqualTo("\"\\u0034\\u0032\""));
    }

    [Test]
    public void SchemaValidationAndArrayMaterializationUseDecodedValues()
    {
        var schema = new SchemaDescriptorBuilder().Named().WithField<string>("text")
            .WithField<int>("number").WithField<int[]>("items").Build();
        using var reader = Open(["""{"text":"line\nfeed","number":"\u0034\u0032","items":["\u0031","\u0032"]}"""], schema: schema);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid(1), Is.True);
        Assert.That(reader.IsValid(2), Is.True);
        Assert.That(reader.GetValue(1), Is.EqualTo(42));
        Assert.That(reader.GetValue(2), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(Fields(reader)[2].Children![0].DecodedValue, Is.EqualTo("1"));
    }

    [Test]
    public void DataTableMaterializationReceivesDecodedValues()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"text":"line\nfeed"}"""));
        var table = typeof(TProfile) == typeof(NdjsonProfile)
            ? new NdjsonReaderBuilder().Build().ToDataTable(stream)
            : new JsonReaderBuilder().Build().ToDataTable(stream);
        Assert.That(table.Rows[0]["text"], Is.EqualTo("line\nfeed"));
    }

    [Test]
    public void RepeatedStringAccessDoesNotAllocateAgain()
    {
        using var reader = Open(["""{"text":"line\nfeed","number":1,"items":[]}"""]);
        Assert.That(reader.Read(), Is.True);
        string first = reader.GetString(0);
        for (int i = 0; i < 100; i++) reader.GetString(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) reader.GetString(0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.LessThan(1024));
        Assert.That(reader.GetString(0), Is.SameAs(first));
    }

    [Test]
    public void CustomStringPoolIsStillApplied()
    {
        int calls = 0;
        using var reader = Open(["""{"text":"line\nfeed","number":1,"items":[]}"""],
            pool: value => { calls++; return value.ToString().ToUpperInvariant(); });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("LINE\nFEED"));
        Assert.That(reader.GetString(0), Is.EqualTo("LINE\nFEED"));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void DifferentShapesDoNotCarryDecodedValuesIntoLaterRows()
    {
        using var reader = Open([
            """{"text":"first\nvalue","number":1,"items":[]}""",
            """{"number":2,"text":"second\tvalue"}""",
            """{"text":null}""",
            """{"text":"plain","number":3,"items":[]}"""
        ]);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("first\nvalue"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("text")), Is.EqualTo("second\tvalue"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("plain"));
    }

    private static FieldSpan[] Fields(BaseDataReader<TProfile> reader)
        => (FieldSpan[])typeof(BaseRawRecord<TProfile>).GetProperty("CurrentFieldSpans", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;

    private static BaseDataReader<TProfile> Open(string[] rows, string mode = "Default", PoolString? pool = null, SchemaDescriptor? schema = null)
    {
        bool ndjson = typeof(TProfile) == typeof(NdjsonProfile);
        string content = mode == "Path" ? "{\"rows\":[" + string.Join(',', rows) + "]}"
            : ndjson ? string.Join('\n', rows) + "\n" : "[" + string.Join(',', rows) + "]";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var options = new ParserOptimizationOptions(BufferSize: 16, ReadAhead: false, PoolString: pool);
        string[]? projected = mode is "Projection" or "Ordered" ? ["text", "number", "items"] : null;
        object reader = ndjson
            ? new PocketCsvReader.Ndjson.NdjsonDataReader(stream, new NdjsonProfile(
                new NdjsonDialectDescriptor(LineTerminator: "\n"), schema: schema, parserOptimizations: options,
                projectedProperties: projected, orderedProjection: mode == "Ordered", recordPath: mode == "Path" ? "$.rows[*]" : null))
            : new JsonDataReader(stream, new JsonProfile(schema: schema, parserOptimizations: options,
                projectedProperties: projected, orderedProjection: mode == "Ordered", recordPath: mode == "Path" ? "$.rows[*]" : null));
        return (BaseDataReader<TProfile>)reader;
    }
}

public class LazyDecodingDerivedReaderTests
{
    [Test]
    public void DerivedValueTransformationsArePreserved([Values(false, true)] bool ndjson)
    {
        const string content = """{"text":"line\nfeed","empty":"ni\u006c"}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        if (ndjson)
        {
            using var reader = new TransformingNdjsonReader(stream);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("LINE\nFEED"));
            Assert.That(reader.IsDBNull(1), Is.True);
        }
        else
        {
            using var reader = new TransformingJsonReader(stream);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("LINE\nFEED"));
            Assert.That(reader.IsDBNull(1), Is.True);
        }
    }

    private class TransformingJsonReader(Stream stream) : JsonDataReader(stream, new JsonProfile())
    {
        protected override NullableSpan GetValueOrThrow(int i)
        {
            var value = base.GetValueOrThrow(i);
            if (!value.HasValue || value.Value.SequenceEqual("nil")) return default;
            return value.Value.ToString().ToUpperInvariant().AsMemory();
        }
    }

    private class TransformingNdjsonReader(Stream stream) : PocketCsvReader.Ndjson.NdjsonDataReader(stream, NdjsonProfile.Default)
    {
        protected override NullableSpan GetValueOrThrow(int i)
        {
            var value = base.GetValueOrThrow(i);
            if (!value.HasValue || value.Value.SequenceEqual("nil")) return default;
            return value.Value.ToString().ToUpperInvariant().AsMemory();
        }
    }
}
