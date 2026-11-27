using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class IndexedLineBatchTests
{
    [Test]
    public async Task KeepsPositionsAndPoolsEscapesAcrossMixedReads(
        [Values(1, 17, 65536)] int size, [Values("\n", "\r\n")] string terminator)
    {
        const string first = """{"used":"one\n\"two\"","ignored":1}""";
        const string next = """{"used":"one\u000a\"two\"","ignored":22}""";
        var rows = new List<string> { first };
        rows.AddRange(Enumerable.Repeat(next, 300));
        var profile = new NdjsonProfile(new(LineTerminator: terminator),
            parserOptimizations: new(BufferSize: size) { PoolStringLastValueHint = true, TrackPosition = true });
        using var reader = new NdjsonDataReader(Input(string.Join(terminator, rows)), profile);

        long offset = 0;
        string? text = null;
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.That(i % 2 == 0 ? reader.Read() : await reader.ReadAsync(), Is.True);
            var current = reader.GetString(reader.GetOrdinal("used"));
            if (text is null)
                Assert.That(current, Is.EqualTo("one\n\"two\""));
            else
                Assert.That(current, Is.SameAs(text));
            text = current;
            Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(offset));
            Assert.That(reader.CurrentPosition.Value.PhysicalLine, Is.EqualTo(i + 1));
            offset += rows[i].Length + terminator.Length;
        }
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task UncertainQuoteStateUsesExistingFramerDiagnostics(string terminator)
    {
        var data = """{"id":1}""" + terminator + "{\"s\":\"raw\\\"" + terminator + "newline\"}" + terminator;
        using var indexed = new NdjsonDataReader(Input(data), new NdjsonProfile(terminator));
        using var fallback = new NdjsonDataReader(Input(data), new NdjsonProfile(new NdjsonDialectDescriptor(LineTerminator: terminator, CommentChar: '#')));
        Assert.That(indexed.Read(), Is.True);
        Assert.That(await fallback.ReadAsync(), Is.True);
        var expected = await Assert.ThrowsAsync<InvalidDataException>(async () => await fallback.ReadAsync());
        var actual = await Assert.ThrowsAsync<InvalidDataException>(async () => await indexed.ReadAsync());
        Assert.That(actual!.Message, Is.EqualTo(expected!.Message));
    }

    [Test]
    public void CrLfDialectKeepsLoneLfInsideARecord()
    {
        using var reader = new NdjsonDataReader(Input("{\n\"id\":42}\r\n{\"id\":7}"), new NdjsonProfile("\r\n"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(7));
        Assert.That(reader.Read(), Is.False);
    }

    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
}
