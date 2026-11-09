using NUnit.Framework;

namespace PocketCsvReader.Json.Testing;

public class ColumnValueScanningTests
{
    [TestCase("123456789")]
    [TestCase("""{"value":[123456789]}""")]
    public void ScanningLargeColumnDoesNotAllocatePerValue(string value)
    {
        const int count = 4096;
        var column = "[" + string.Join(",", Enumerable.Repeat(value, count)) + "]";
        Scan(column);
        var start = GC.GetAllocatedBytesForCurrentThread();
        var cells = Scan(column);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.That(cells, Is.EqualTo(count));
        // The ceiling excludes a complete copy of the character frame and any per-cell metadata.
        Assert.That(allocated, Is.LessThan(1024));
    }

    [Test]
    public void ScanningPreservesRawPropertyAndValueRanges()
    {
        const string json = """ { "la\u0062el" : [1, {"nested":"x\"y"}, null], "flag": true } """;
        var properties = JsonRecordParser.Enumerate(json, properties: true);

        Assert.That(properties.MoveNext(), Is.True);
        Assert.That(properties.Name.ToString(), Is.EqualTo("\"la\\u0062el\""));
        Assert.That(properties.Value.ToString(), Is.EqualTo("""[1, {"nested":"x\"y"}, null]"""));

        Assert.That(properties.MoveNext(), Is.True);
        Assert.That(properties.Value.ToString(), Is.EqualTo("true"));
        Assert.That(properties.MoveNext(), Is.False);
    }

    [TestCase("""{"values":[1,]}""")]
    [TestCase("""{"values":[1 2]}""")]
    [TestCase("""{"values":[{"key" "value"}]}""")]
    [TestCase("""{"values":[{"key":"\q"}]}""")]
    [TestCase("""{"values":[{"key":01}]}""")]
    public void ScanningAndMaterializingReportTheSameGrammarErrors(string json)
    {
        var materialized = Assert.Throws<InvalidDataException>(() => JsonRecordParser.Parse(json, [' ', '\t', '\r', '\n']));
        var scanned = Assert.Throws<InvalidDataException>(() => Scan(json, properties: true));
        Assert.That(scanned?.Message, Is.EqualTo(materialized?.Message));
    }

    private static int Scan(string json, bool properties = false)
    {
        var values = JsonRecordParser.Enumerate(json, properties);
        var cells = 0;
        while (values.MoveNext())
            cells++;
        return cells;
    }
}
