using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class LazyStableDecodingTests
{
    [Test]
    public async Task StableShapeResetsDecodedValuesInReusedBuffers(
        [Values(false, true)] bool async, [Values(false, true)] bool projection)
    {
        var builder = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithStableObjectShape().WithParserOptimizations(new(BufferSize: 16, ReadAhead: false));
        if (projection) builder.WithProjection(p => p.Property("text").Property("items"));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n',
            Enumerable.Range(0, 4).Select(i => $"{{\"text\":\"row{i}\\n\",\"items\":[\"item{i}\\t\"],\"unused\":\"skip\\u002B\"}}"))));
        using var reader = builder.Build().ToDataReader(stream);
        for (int i = 0; i < 4; i++)
        {
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            var fields = (FieldSpan[])typeof(BaseRawRecord<NdjsonProfile>)
                .GetProperty("CurrentFieldSpans", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader)!;
            Assert.That(fields[0].DecodedValue, Is.Null);
            Assert.That(fields[1].Children![0].DecodedValue, Is.Null);
            Assert.That(reader.GetString(0), Is.EqualTo($"row{i}\n"));
            Assert.That(reader.GetArrayItem<string>(1, 0), Is.EqualTo($"item{i}\t"));
            if (!projection) Assert.That(fields[2].DecodedValue, Is.Null);
        }
    }

    [TestCase("\\q")]
    [TestCase("\\u12")]
    [TestCase("\\uD800")]
    [TestCase("\\uDC00")]
    public async Task StableShapeValidatesUntouchedValuesOnFollowingRows(string invalid)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"text\":\"ok\",\"unused\":\"fine\"}\n{\"text\":\"ok\",\"unused\":\"" + invalid + "\"}"));
        using var reader = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithStableObjectShape().Build().ToDataReader(stream);
        Assert.That(await reader.ReadAsync(), Is.True);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync());
    }
}
