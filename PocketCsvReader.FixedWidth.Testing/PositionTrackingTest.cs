using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class PositionTrackingTest
{
    [TestCase(false, false)] [TestCase(false, true)]
    [TestCase(true, false)] [TestCase(true, true)]
    public async Task TracksConfiguredRecords(bool enabled, bool async)
    {
        using var reader = new FixedWidthReaderBuilder().WithPositionTracking(enabled)
            .WithParserOptimizations(new(BufferSize: 1)).WithLineTerminator("\r\n")
            .WithField("Value", 0, 2, FixedWidthPadding.None).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("😀\r\néx")));
        Assert.That(reader.CurrentPosition, Is.Null);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(1, 1, 0, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(2, 2, 4, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }
}
