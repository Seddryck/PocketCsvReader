using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class PositionTrackingTest
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CommentsBlankLinesAndUnicodeRecords(bool enabled, bool async)
    {
        const string prefix = "#skip\r\n\r\n";
        const string first = "{\"a\":\"😀\",\"b\":1}\r\n";
        var profile = new NdjsonProfile(new(LineTerminator: "\r\n", CommentChar: '#'),
            parserOptimizations: new(BufferSize: 1) { TrackPosition = enabled });
        using var reader = new NdjsonReader(profile).ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + first + "{\"a\":2}")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(1, 3, prefix.Length, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(enabled ? new RecordPosition(2, 4, prefix.Length + first.Length, PositionOffsetUnit.Utf16CodeUnits) : (RecordPosition?)null));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectedRowsKeepTheirSourceOffsets(bool async)
    {
        const string prefix = "#skip\n{\"rows\":[";
        const string first = "{\"a\":1},";
        using var reader = new NdjsonReaderBuilder().WithPositionTracking().WithRecordPath("$.rows[*]")
            .WithDialect(d => d.WithLineTerminator("\n").WithCommentChar('#'))
            .WithParserOptimizations(new(BufferSize: 2)).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + first + "{\"a\":2}]}\n")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(1, 2, prefix.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 2, prefix.Length + first.Length, PositionOffsetUnit.Utf16CodeUnits)));
    }
    [Test]
    public void SelectedRowsDoNotDoubleCountCrLfAcrossCustomTerminator()
    {
        const string first = "{\"rows\":[{\"a\":1}]}\r";
        using var reader = new NdjsonReaderBuilder().WithPositionTracking().WithRecordPath("$.rows[*]")
            .WithDialect(d => d.WithLineTerminator("\r"))
            .WithParserOptimizations(new(BufferSize: 1)).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(first + "\n{\"rows\":[{\"a\":2}]}")));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 2, first.Length + 10, PositionOffsetUnit.Utf16CodeUnits)));
    }
}
