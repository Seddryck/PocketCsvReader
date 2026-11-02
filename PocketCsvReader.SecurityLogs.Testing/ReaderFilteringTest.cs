using System.Text;
using NUnit.Framework;
using PocketCsvReader.SecurityLogs.Configuration;

namespace PocketCsvReader.SecurityLogs.Testing;

public class ReaderFilteringTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Filters_ApplyToSecurityLogs(bool async)
    {
        var csv = new CefReaderBuilder().WithAttributes("src").WithFilter("src", "10.0.0.2").Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "CEF:0|Vendor|Product|1|100|Test|5|src=10.0.0.1\nCEF:0|Vendor|Product|1|100|Test|5|src=10.0.0.2"));
        using var reader = csv.ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("src")), Is.EqualTo("10.0.0.2"));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }
}
