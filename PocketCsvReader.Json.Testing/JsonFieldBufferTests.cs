using NUnit.Framework;

namespace PocketCsvReader.Json.Testing;

public class JsonFieldBufferTests
{
    private static readonly char[] Whitespace = [' ', '\t', '\r', '\n'];

    [Test]
    public void EmptyObjects_KeepTheAllocationFreeOwnedParserPath()
    {
        for (int i = 0; i < 100; i++) JsonRecordParser.Parse("{}", Whitespace);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) JsonRecordParser.Parse("{}", Whitespace);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.LessThan(1024));
    }

    [Test]
    public void RepeatedFlatObjects_DoNotAllocateMetadataAfterWarmup()
    {
        const string json = """{"host":"192.0.2.1","identity":"-","user":"user1","timestamp":"now","request":"GET / HTTP/1.1","status":200,"bytes":1024}""";
        var buffer = new JsonFieldBuffer();
        for (int i = 0; i < 100; i++)
            JsonRecordParser.Parse(json, Whitespace, buffer: buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            JsonRecordParser.Parse(json, Whitespace, buffer: buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            JsonRecordParser.Parse(json, Whitespace);
        long independentlyOwned = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"Metadata over 1000 seven-field objects: owned={independentlyOwned} bytes, reused={allocated} bytes; FieldSpan size={System.Runtime.CompilerServices.Unsafe.SizeOf<FieldSpan>()} bytes.");

        Assert.That(allocated, Is.LessThan(1024), "Repeated flat rows should reuse metadata storage.");
    }

    [Test]
    public void RepeatedEscapes_ReuseDecodingStorageAndKeepIndependentStrings()
    {
        const string json = """{"na\u006de":"Ada\nLovelace","timestamp":"01/Nov/2026:12:34:56 \u002B0000"}""";
        var buffer = new JsonFieldBuffer();
        var first = JsonRecordParser.Parse(json, Whitespace, buffer: buffer);
        string label = first[0].DecodedLabel!;
        Assert.That(first[0].DecodedValue, Is.Null);
        string value = JsonRecordParser.DecodeString(json.AsSpan().Slice(first[0].Value.Start - 1, first[0].Value.Length + 2), buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) JsonRecordParser.Parse(json, Whitespace, buffer: buffer);
        long reused = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) JsonRecordParser.Parse(json, Whitespace);
        long owned = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(reused, Is.LessThan(owned / 3));
        Assert.That(label, Is.EqualTo("name"));
        Assert.That(value, Is.EqualTo("Ada\nLovelace"));
        AssertFields(first, JsonRecordParser.Parse(json, Whitespace));
        TestContext.WriteLine($"Escaped rows over 1000 objects: owned={owned} bytes, reused={reused} bytes.");
    }

    [Test]
    public void Reuse_HandlesChangingShapesEscapesAndNestedValues()
    {
        string[] records =
        [
            """{"id":1,"name":"Ada"}""",
            """{"na\u006de":"line\nfeed","items":[null,{"ok":true},[1,2]],"id":2}""",
            "{}", "[]", "42", "null", "\"escaped\\tvalue\"",
            """{"new":false}""",
            """{"id":3,"name":"Grace"}"""
        ];
        var buffer = new JsonFieldBuffer();
        foreach (string record in records)
        {
            var expected = JsonRecordParser.Parse(record, Whitespace);
            var actual = JsonRecordParser.Parse(record, Whitespace, buffer: buffer);
            AssertFields(actual, expected);
            Assert.That(buffer.Scratch, Is.Empty);
        }
    }

    [TestCase("""{"id":2,"name":"new"} trailing""")]
    [TestCase("""{"id":2,"name":"new","broken":]}""")]
    [TestCase("""{"id":2,"name":"bad\q"}""")]
    public void InvalidRecord_DoesNotOverwritePublishedFields(string invalid)
    {
        var buffer = new JsonFieldBuffer();
        var published = JsonRecordParser.Parse("""{"id":1,"name":"old"}""", Whitespace, buffer: buffer);
        var snapshot = published.ToArray();

        Assert.Throws<InvalidDataException>(() => JsonRecordParser.Parse(invalid, Whitespace, buffer: buffer));
        AssertFields(published, snapshot);
        Assert.That(buffer.Scratch, Is.Empty);
        AssertFields(JsonRecordParser.Parse("""{"ok":true}""", Whitespace, buffer: buffer),
            JsonRecordParser.Parse("""{"ok":true}""", Whitespace));
    }

    [Test]
    public void Jsonc_ReusedBufferPreservesCommentsAndTrailingCommaValidation()
    {
        const string json = """{/*comment*/"name":"Ada","items":[1,2,],}""";
        var buffer = new JsonFieldBuffer();
        for (int i = 0; i < 3; i++)
            AssertFields(JsonRecordParser.Parse(json, Whitespace, true, true, buffer),
                JsonRecordParser.Parse(json, Whitespace, true, true));
        Assert.Throws<InvalidDataException>(() => JsonRecordParser.Parse(json, Whitespace, buffer: buffer));
    }

    private static void AssertFields(FieldSpan[] actual, FieldSpan[] expected)
    {
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.That(actual[i] with { Children = null }, Is.EqualTo(expected[i] with { Children = null }));
            if (expected[i].Children is { } children)
            {
                Assert.That(actual[i].Children, Is.Not.Null);
                AssertFields(actual[i].Children!, children);
            }
            else
                Assert.That(actual[i].Children, Is.Null);
        }
    }
}
