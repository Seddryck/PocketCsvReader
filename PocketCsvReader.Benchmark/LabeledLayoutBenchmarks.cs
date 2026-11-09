using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.KeyValue;
using PocketCsvReader.KeyValue.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Benchmark;

public static class LabeledLayoutBenchmarks
{
    private const int RecordCount = 4_096;
    private const string Id = "id";
    private const string Duration = "duration";

    public static void RunMatrix()
    {
        Console.WriteLine("Format,Input,Mode,CpuMsPer1000Rows,WallMsPer1000Rows,BytesPerRow");
        MeasureKeyValue("Logfmt", true, () => new LogfmtReaderBuilder(), builder => builder.Build());
        MeasureKeyValue("LTSV", false, () => new LtsvReaderBuilder(), builder => builder.Build());
        MeasureJson();
    }

    private static void MeasureKeyValue<TBuilder>(
        string format, bool logfmt, Func<TBuilder> create, Func<TBuilder, KeyValueReader> build)
        where TBuilder : KeyValueReaderBuilder<TBuilder>
    {
        foreach (var varying in new[] { true, false })
        {
            var data = BuildKeyValueInput(logfmt, varying);
            var input = varying ? "Varying" : "Fixed";
            var ordinary = build(create());
            var projected = build(WithProjection(create()));
            Measure(format, input, "Ordinary", () => Read(ordinary, data));
            Measure(format, input, "Projection", () => Read(projected, data));
            if (varying)
                continue;

            var stable = build(create().WithStableFieldLayout());
            var combined = build(WithProjection(create()).WithStableFieldLayout());
            Measure(format, input, "Stable", () => Read(stable, data));
            Measure(format, input, "ProjectionAndStable", () => Read(combined, data));
        }

        static TBuilder WithProjection(TBuilder builder)
            => builder.WithProjection(p => p.Column(Id).Column(Duration));
    }

    private static void MeasureJson()
    {
        const string json = """{"\u0069d":42,"duration":18,"trace":{"tags":[1,2,3],"active":true},"region":"west","status":"ok","request":"GET","message":"test"}""";
        var array = Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat(json, RecordCount)) + "]");
        var lines = Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Repeat(json, RecordCount)));
        var ordinaryJson = new JsonReaderBuilder().Build();
        var stableJson = new JsonReaderBuilder().WithStableFieldLayout().Build();
        var ordinaryNdjson = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).Build();
        var stableNdjson = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).WithStableFieldLayout().Build();
        Measure("JSON", "ObjectArray", "Ordinary", () => Read(ordinaryJson, array));
        Measure("JSON", "ObjectArray", "Stable", () => Read(stableJson, array));
        Measure("NDJSON", "Fixed", "Ordinary", () => Read(ordinaryNdjson, lines));
        Measure("NDJSON", "Fixed", "Stable", () => Read(stableNdjson, lines));
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

    private static long Read<TProfile, TReader>(FlatFileReader<TProfile, TReader> format, byte[] data)
        where TProfile : IProfile
        where TReader : IDataReader
    {
        using var reader = format.ToDataReader(new MemoryStream(data, writable: false));
        return Sum(reader);
    }

    private static long Sum(IDataReader reader)
    {
        long checksum = 0;
        var rows = 0;
        while (reader.Read())
        {
            checksum += reader.GetInt32(reader.GetOrdinal(Id)) + reader.GetInt32(reader.GetOrdinal(Duration));
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
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{format},{input},{mode},{cpu[median]:F3},{wall[median]:F3},{allocation[median]:F2}"));
    }
}
