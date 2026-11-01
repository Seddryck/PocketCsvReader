using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class PositionTrackingTest
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ArrayElementsHaveTheirOwnPositions(bool enabled, bool async)
    {
        const string first = "[\r\n{\"a\":\"😀\"},\r\n";
        using var reader = new JsonReaderBuilder().WithPositionTracking(enabled)
            .WithParserOptimizations(new(BufferSize: 1)).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(first + "{\"a\":2}]")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(1, 2, 3, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(2, 3, first.Length, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectedNodesRetainSourcePositions(bool async)
    {
        const string prefix = "{\"rows\":[\r\n";
        const string first = "{\"a\":1},\r\n";
        using var reader = new JsonReaderBuilder().WithPositionTracking().WithRecordPath("$.rows[*]")
            .WithParserOptimizations(new(BufferSize: 2)).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + first + "{\"a\":2}]}")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(1, 2, prefix.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 3, prefix.Length + first.Length, PositionOffsetUnit.Utf16CodeUnits)));
    }
}
