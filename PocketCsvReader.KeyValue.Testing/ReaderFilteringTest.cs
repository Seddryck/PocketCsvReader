using System.Text;
using NUnit.Framework;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class ReaderFilteringTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Filters_RebindNames_AndShareParsedValues(bool async)
    {
        var calls = 0;
        ParseSpan<int> parse = span => { calls++; return int.Parse(span); };
        var csv = new LogfmtReaderBuilder().WithParsers(p => p.WithParser(parse))
            .WithFilter<int>("id", id => id > 1).Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("id=1 name=Ada\nname=Bob id=2"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int>("id"), Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }
}
