using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class CurrentRecordViewTests
{
    private static FixedWidthReader CreateReader() => new FixedWidthReaderBuilder()
        .WithLineTerminator("\n").WithField("Request", 0, 8)
        .WithField("Status", 8, 3).WithField("Bytes", 11, 4).Build();

    [Test]
    public void SynchronousFields_DoNotForceARecordMemoryWrapper()
    {
        using var reader = CreateReader().ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("request 2001024\nsecond  4042048\n")));
        var wrapper = typeof(BaseRawRecord<FixedWidthProfile>).GetField("_record", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(reader.Read(), Is.True);
        string retained = reader.GetString(0);
        Assert.That(retained, Is.EqualTo("request"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(200));
        Assert.That(reader.GetRawString(2), Is.EqualTo("1024"));
        Assert.That(wrapper.GetValue(reader), Is.Null);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("second"));
        Assert.That(wrapper.GetValue(reader), Is.Null);
        Assert.That(retained, Is.EqualTo("request"));
    }

    [Test]
    public void RawSynchronousReader_HasABoundedSetupAllocationBudget()
    {
        const int rows = 2000;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("request 2001024\n", rows)));
        int ReadAll()
        {
            using var reader = CreateReader().ToDataReader(new MemoryStream(data));
            int count = 0;
            while (reader.Read()) count++;
            return count;
        }
        for (int i = 0; i < 3; i++) ReadAll();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = ReadAll();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(count, Is.EqualTo(rows));
        // Includes setup; catches an 80-byte wrapper for every raw row.
        Assert.That(allocated, Is.LessThan(32L * rows), $"Allocated {allocated} bytes.");
    }
}
