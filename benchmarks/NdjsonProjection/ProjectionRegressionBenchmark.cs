using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Benchmark;

internal static class ProjectionRegressionBenchmark
{
    public static async Task Run(string mode, string label)
    {
        bool asynchronous = mode == "async-json";
        int count = asynchronous ? 5000 : 1;
        string row = asynchronous ? "{\"id\":7,\"text\":\"ok\"}"
            : "{\"id\":7,\"unused\":\"" + new string(mode == "spaces" ? ' ' : 'x', 8 * 1024 * 1024) + "\",\"text\":\"ok\"}";
        byte[] input = Encoding.UTF8.GetBytes(asynchronous
            ? "[" + string.Join(',', Enumerable.Repeat(row, count)) + "]" : row + "\n");
        IAsyncDataReader Open() => asynchronous
            ? new JsonReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(new(BufferSize: 64, ReadAhead: false))
                .WithProjection(p => p.Property("id").Property("text")).Build().ToDataReader(new YieldStream(input))
            : new NdjsonReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithDialect(d => d.WithLineTerminator("\n"))
                .WithParserOptimizations(new(BufferSize: 4096, ReadAhead: false))
                .WithProjection(p => p.Property("id").Property("text")).Build().ToDataReader(new MemoryStream(input));
        // Verify both selected values on every row outside the measurement.
        using (var reader = Open())
        {
            int seen = 0;
            while (asynchronous ? await reader.ReadAsync() : reader.Read())
            {
                if (reader.GetInt32(0) != 7 || reader.GetString(1) != "ok") throw new InvalidDataException("Bad value");
                seen++;
            }
            if (seen != count) throw new InvalidDataException("Row count");
        }
        async Task<long> Read()
        {
            using var reader = Open();
            long sum = 0;
            while (asynchronous ? await reader.ReadAsync() : reader.Read()) sum += reader.GetInt32(0);
            return sum;
        }
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed.TotalSeconds < 2)
            if (await Read() != count * 7L) throw new InvalidDataException("Warmup");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        var cpu = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        int operations = 0;
        do
        {
            if (await Read() != count * 7L) throw new InvalidDataException("Checksum");
            operations++;
        } while (timer.Elapsed.TotalSeconds < 2);
        timer.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            label, mode, rows = count, operations,
            cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds / operations,
            elapsedMs = timer.Elapsed.TotalMilliseconds / operations,
            allocatedBytes = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / operations,
            inputHash = Convert.ToHexString(SHA256.HashData(input)), runtime = Environment.Version.ToString()
        }));
    }

    private sealed class YieldStream(byte[] data) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync I/O");
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(Math.Min(64, buffer.Length), data.Length - _position);
            data.AsMemory(_position, length).CopyTo(buffer);
            _position += length;
            return length;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
