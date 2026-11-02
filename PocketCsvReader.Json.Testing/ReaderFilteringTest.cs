using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class ReaderFilteringTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Filters_RebindShapes_AndReuseProjectionValues(bool async)
    {
        var calls = 0;
        ParseSpan<int> parse = span => { calls++; return int.Parse(span); };
        var csv = new JsonReaderBuilder()
            .WithSchema(s => s.Named().WithField<int>("id", f => f.WithParser(parse)).WithField<string>("name"))
            .WithFilter<int>("id", id => id > 1).WithFilter("name", "Ada").Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "[{\"id\":1,\"name\":null},{\"name\":\"A\\u0064a\",\"id\":2},{\"id\":3,\"name\":\"Bob\"}]"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetValue("id"), Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(calls, Is.EqualTo(3));
    }
}
