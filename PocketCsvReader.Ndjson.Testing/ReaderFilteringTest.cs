using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class ReaderFilteringTest
{
    [Test]
    public void StableShape_SupportsNullableTypedFiltering()
    {
        var csv = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).WithStableObjectShape()
            .WithFilter<int?>("id", id => id is null || id > 1).Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}\n{\"id\":null}\n{\"id\":2}"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int?>("id"), Is.Null);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int?>("id"), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Filters_RebindChangingShapes_AndDecodeJsonStrings(bool async)
    {
        var csv = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).WithFilter<int>("id", id => id > 1)
            .WithFilter("name", value => value.SequenceEqual("Ada")).Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"id\":1,\"name\":null}\n{\"name\":\"A\\u0064a\",\"id\":2}\n{\"id\":3,\"name\":\"Bob\"}"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int>("id"), Is.EqualTo(2));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }
}
