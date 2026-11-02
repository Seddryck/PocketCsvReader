using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class ReaderFilteringTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Filters_UseTrimmedFields_AndTypedGetters(bool async)
    {
        var csv = new FixedWidthReaderBuilder().WithLineTerminator("\n")
            .WithField("Id", 0, 3).WithField("Name", 3, 3)
            .WithFilter<int>("Id", id => id > 1).WithFilter("Name", "Bob").Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1  Ada\n2  Bob\n"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }
}
