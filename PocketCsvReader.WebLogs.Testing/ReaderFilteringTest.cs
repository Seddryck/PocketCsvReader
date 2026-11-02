using System.Text;
using NUnit.Framework;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.WebLogs.Testing;

public class ReaderFilteringTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Filters_ApplyToWebLogMaterialization(bool async)
    {
        var csv = new CommonLogReaderBuilder().WithFilter<int>(5, status => status >= 400).Build();
        const string prefix = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET / HTTP/1.0\" ";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(prefix + "200 42\n" + prefix + "404 0\n"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetInt32(5), Is.EqualTo(404));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }
}
