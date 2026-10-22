using System.Text;
using NUnit.Framework;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.WebLogs.Testing;

public class PositionTrackingTest
{
    [TestCase(false, false)] [TestCase(false, true)]
    [TestCase(true, false)] [TestCase(true, true)]
    public async Task DirectivesAndBlankLinesOnlyAffectSourcePosition(bool enabled, bool async)
    {
        const string prefix = "#Version: 1.0\r\n\r\n#Fields: value\n";
        using var reader = new W3cExtendedLogReaderBuilder().WithPositionTracking(enabled)
            .WithParserOptimizations(new(BufferSize: 1)).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + "😀\r\né")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(1, 4, prefix.Length, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(2, 5, prefix.Length + 4, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [Test]
    public void CommonLogUsesSameContract()
    {
        const string row = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET / HTTP/1.0\" 200 42";
        using var reader = new CommonLogReaderBuilder().WithPositionTracking().Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("\n" + row)));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(1, 2, 1, PositionOffsetUnit.Utf16CodeUnits)));
    }
}
