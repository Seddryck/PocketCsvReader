using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class BufferedLineBatchTests
{
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(65536)]
    public async Task ReadsIndexedBatchesAndSplitLinesWithMixedTransport(int size)
    {
        var tracker = new PositionTracker();
        var rows = new List<(string Content, string Terminator)>
        {
            ("café", "\r\n"), ("", "\n"), ("wide" + new string('x', 240), "\r"),
        };
        rows.AddRange(Enumerable.Range(0, 300).Select(i => ($"row{i}", i % 2 == 0 ? "\r\n" : "\n")));
        rows.Add(("last", ""));
        using var text = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(
            string.Concat(rows.Select(row => row.Content + row.Terminator)))));
        using var buffer = new BufferedRecordReader(text, size, tracker: tracker);

        int offset = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            buffer.BeginOperation();
            tracker.BeginRecord();
            var capture = i % 2 == 0 ? buffer.ReadLineFrame() : await buffer.ReadLineFrameAsync(default);
            var row = rows[i];
            Assert.That(capture!.Value.Memory.ToString(), Is.EqualTo(row.Content));
            tracker.CompleteRecord();
            Assert.That(tracker.CurrentPosition!.Value.Offset, Is.EqualTo(offset));
            Assert.That(tracker.CurrentPosition.Value.PhysicalLine, Is.EqualTo(i + 1));
            offset += row.Content.Length + row.Terminator.Length;
        }

        buffer.BeginOperation();
        Assert.That(await buffer.ReadLineFrameAsync(default), Is.Null);
        Assert.That(buffer.Position, Is.EqualTo(offset));
    }
}
