using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class LabeledStringReuseTests
{
    [Test]
    public async Task ReusesLogicalStringAcrossAlternateEscapesAndMixedReads()
    {
        const string document = """
            [{"City":"Caf\u00e9 \ud83d\ude00","Other":"first"},
             {"City":"Café 😀","Other":"second"},
             {"Other":"third","City":"Café 😀"},
             {"City":"Caf\u00e9 \ud83d\ude00"}]
            """;
        using var reader = new JsonDataReader(Input(document),
            new JsonProfile(parserOptimizations: new(BufferSize: 11) { PoolStringLastValueHint = true }));
        Assert.That(reader.Read(), Is.True);
        var first = reader.GetString(0);
        Assert.That(first, Is.EqualTo("Café 😀"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.SameAs(first));
        Assert.That(reader.GetString(1), Is.EqualTo("second"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("City"), Is.EqualTo(1));
        Assert.That(reader.GetString(0), Is.EqualTo("third"));
        Assert.That(reader.GetString(1), Is.EqualTo(first));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.GetString(0), Is.EqualTo(first));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CustomPoolReceivesDecodedValuesAndKeepsTransformations(bool transform)
    {
        int calls = 0;
        string Pool(ReadOnlySpan<char> value)
        {
            calls++;
            Assert.That(value.ToString(), Is.EqualTo("a\nb"));
            return transform ? value.ToString().ToUpperInvariant() : value.ToString();
        }

        using var reader = new JsonDataReader(Input("""[{"v":"a\nb"},{"v":"a\u000ab"}]"""),
            new JsonProfile(parserOptimizations: new(PoolString: Pool) { PoolStringLastValueHint = true }));
        Assert.That(reader.Read(), Is.True);
        var first = reader.GetString(0);
        Assert.That(reader.Read(), Is.True);
        var second = reader.GetString(0);
        Assert.That(second, Is.EqualTo(transform ? "A\nB" : "a\nb"));
        if (!transform)
            Assert.That(second, Is.SameAs(first));
        Assert.That(calls, Is.EqualTo(transform ? 2 : 1));
    }

    [Test]
    public async Task PublicMetadataOwnsNestedFieldsAcrossBorrowedReads()
    {
        const string document = """[{"obj":{"id":1}},{"obj":{"id":22}},{"obj":{"id":333}},{"obj":{"id":4444}}]""";
        using var text = new StreamReader(Input(document));
        using var source = new JsonRecordSource(text, new JsonProfile { StableFieldLayout = true });
        var borrowed = (ICurrentRecordSource)source;

        var first = ReadPublic(source);
        var second = (await ((IRecordSource<JsonProfile>)source).ReadAsync()).Record.FieldSpans;
        var third = borrowed.ReadCurrent().Record.Fields!;
        var fourth = (await borrowed.ReadCurrentAsync(default)).Record.Fields!;
        Assert.That(first[0].Children![0].Value.Length, Is.EqualTo(1));
        Assert.That(second[0].Children![0].Value.Length, Is.EqualTo(2));
        Assert.That(third[0].Children![0].Value.Length, Is.EqualTo(3));
        Assert.That(fourth[0].Children![0].Value.Length, Is.EqualTo(4));

        static FieldSpan[] ReadPublic(JsonRecordSource source)
        {
            source.IsEndOfFile(out var record, out _);
            return record.FieldSpans;
        }
    }

    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
}
