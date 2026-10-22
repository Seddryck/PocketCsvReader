using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class RowShapeParserCachingTest
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ChangingShapes_DoNotRecompileSchemaParsersPerRow(int parserKind)
    {
        // Allocation comparison guards against expression compilation, without timing assertions.
        // Include more than the 64 cached label shapes to cover uncached row-shape instances.
        var stable = Content(false);
        var changing = Content(true);
        var reader = new JsonReaderBuilder()
            .WithSchema(s => s.Named().WithField<int>("id", f => parserKind switch
            {
                1 => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text)),
                2 => f.WithParser((string text) => int.Parse(text)),
                _ => f
            })).Build();
        Measure(reader, stable);
        Measure(reader, changing);
        var stableBytes = Measure(reader, stable);
        var changingBytes = Measure(reader, changing);
        Assert.That(changingBytes, Is.LessThan(stableBytes * 4),
            "Shape changes should rebind existing schema parsers, not compile new delegates per record.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangingShapes_PreserveDistinctFieldParsersAndNullableFormats(bool asynchronous)
    {
        var source = new JsonReaderBuilder()
            .WithSchema(s => s.Named()
                .WithField<int>("plus", f => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text) + 1))
                .WithField<int>("minus", f => f.WithParser((string text) => int.Parse(text) - 1))
                .WithIntegerField<int?>("optional", f => f.WithFormat(fmt => fmt.WithGroupChar('_'))))
            .Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "[{\"plus\":41,\"minus\":43,\"optional\":\"1_234\"}," +
            "{\"optional\":null,\"minus\":44,\"plus\":42}," +
            "{\"plus\":43,\"minus\":45,\"optional\":\"2_345\"}]"));
        await using var reader = source.ToDataReader(stream);
        for (var row = 0; row < 3; row++)
        {
            Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.GetValue(reader.GetOrdinal("plus")), Is.EqualTo(42 + row));
            Assert.That(reader.GetFieldValue<int>(reader.GetOrdinal("minus")), Is.EqualTo(42 + row));
            Assert.That(reader.GetFieldValue<int?>(reader.GetOrdinal("optional")),
                Is.EqualTo(row == 1 ? (int?)null : row == 0 ? 1234 : 2345));
        }
    }

    private static byte[] Content(bool changing) => Encoding.UTF8.GetBytes("[" + string.Join(',',
        Enumerable.Range(0, 1000).Select(i => changing && i % 2 == 1
            ? $"{{\"label{i % 80}\":\"Ada\",\"id\":{i}}}"
            : $"{{\"id\":{i},\"label{(changing ? i % 80 : 0)}\":\"Ada\"}}")) + "]");

    private static long Measure(JsonReader reader, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(reader.ToObjectArrays(stream).Count(), Is.EqualTo(1000));
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
