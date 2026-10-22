using System.Text;
using NUnit.Framework;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class PositionTrackingTest
{
    [TestCase(false, false)] [TestCase(false, true)]
    [TestCase(true, false)] [TestCase(true, true)]
    public async Task TracksActualLineTerminatorsAndBlankRecords(bool enabled, bool async)
    {
        foreach (bool logfmt in new[] { false, true })
        {
            string first = logfmt ? "a=😀" : "a:😀";
            string last = logfmt ? "a=é" : "a:é";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(first + "\r\n\r" + last));
            using var reader = logfmt
                ? new LogfmtReaderBuilder().WithPositionTracking(enabled).WithParserOptimizations(new(BufferSize: 1)).Build().ToDataReader(stream)
                : new LtsvReaderBuilder().WithPositionTracking(enabled).WithParserOptimizations(new(BufferSize: 1)).Build().ToDataReader(stream);
            foreach (var expected in new[] { new RecordPosition(1, 1, 0, PositionOffsetUnit.Utf16CodeUnits), new(2, 2, 6, PositionOffsetUnit.Utf16CodeUnits), new(3, 3, 7, PositionOffsetUnit.Utf16CodeUnits) })
            {
                Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
                Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? expected : (RecordPosition?)null));
            }
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
            Assert.That(reader.CurrentPosition, Is.Null);
        }
    }
}
