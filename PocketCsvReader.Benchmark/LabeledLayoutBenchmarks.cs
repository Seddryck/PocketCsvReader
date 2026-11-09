using System.Diagnostics;
using System.Text;
using PocketCsvReader.KeyValue;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.Benchmark;

public static class LabeledLayoutBenchmarks
{
    private const int RecordCount = 4_096;

    public static void RunMatrix()
    {
        Console.WriteLine("Format,Input,Mode,CpuMsPer1000Rows,WallMsPer1000Rows,BytesPerRow");
        foreach (var logfmt in new[] { true, false })
        {
            var format = logfmt ? "Logfmt" : "LTSV";
            foreach (var varying in new[] { true, false })
            {
                var data = BuildKeyValueInput(logfmt, varying);
                var input = varying ? "Varying" : "Fixed";
                var ordinary = logfmt ? new LogfmtReaderBuilder().Build() : new LtsvReaderBuilder().Build();
                var projected = logfmt
                    ? new LogfmtReaderBuilder().WithProjection(p => p.Column("id").Column("duration")).Build()
                    : new LtsvReaderBuilder().WithProjection(p => p.Column("id").Column("duration")).Build();
                Measure(format, input, "Ordinary", () => Read(ordinary, data));
                Measure(format, input, "Projection", () => Read(projected, data));
            }
        }
    }

    private static byte[] BuildKeyValueInput(bool logfmt, bool varying)
    {
        var separator = logfmt ? ' ' : '\t';
        var assignment = logfmt ? '=' : ':';
        var data = new StringBuilder();
        for (var row = 0; row < RecordCount; row++)
        {
            var fields = Enumerable.Range(0, 14).Select(i => $"tag{i}{assignment}value").ToList();
            if (varying)
                fields[row % 14] = $"extra{row % 257}{assignment}value";
            fields.Insert(varying ? row % 14 : 6, $"id{assignment}42");
            fields.Insert(varying ? (row + 7) % 15 : 12, $"duration{assignment}18");
            data.AppendJoin(separator, fields).Append('\n');
        }
        return Encoding.UTF8.GetBytes(data.ToString());
    }

    private static long Read(KeyValueReader format, byte[] data)
    {
        using var reader = format.ToDataReader(new MemoryStream(data, writable: false));
        return Sum(reader);
    }

    private static long Sum(System.Data.IDataReader reader)
    {
        long checksum = 0;
        var rows = 0;
        while (reader.Read())
        {
            checksum += reader.GetInt32(reader.GetOrdinal("id")) + reader.GetInt32(reader.GetOrdinal("duration"));
            rows++;
        }
        if (rows != RecordCount)
            throw new InvalidOperationException($"Expected {RecordCount} rows, got {rows}.");
        return checksum;
    }

    private static void Measure(string format, string input, string mode, Func<long> read)
    {
        if (read() != RecordCount * 60)
            throw new InvalidOperationException("Incorrect benchmark values.");

        var warmup = Stopwatch.StartNew();
        while (warmup.ElapsedMilliseconds < 750)
            read();

        using var process = Process.GetCurrentProcess();
        var cpu = new double[5];
        var wall = new double[cpu.Length];
        var allocation = new double[cpu.Length];
        for (var i = 0; i < cpu.Length; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var beforeCpu = process.TotalProcessorTime;
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var count = 0;
            do
            {
                read();
                count++;
            }
            while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 400);
            var elapsed = Stopwatch.GetElapsedTime(start);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            var totalRows = (double)count * RecordCount;
            cpu[i] = (process.TotalProcessorTime - beforeCpu).TotalMilliseconds * 1_000 / totalRows;
            wall[i] = elapsed.TotalMilliseconds * 1_000 / totalRows;
            allocation[i] = bytes / totalRows;
        }
        Array.Sort(cpu);
        Array.Sort(wall);
        Array.Sort(allocation);
        var median = cpu.Length / 2;
        Console.WriteLine(FormattableString.Invariant(
            $"{format},{input},{mode},{cpu[median]:F3},{wall[median]:F3},{allocation[median]:F2}"));
    }
}
