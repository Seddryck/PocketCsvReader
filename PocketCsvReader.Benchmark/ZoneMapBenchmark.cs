using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

namespace PocketCsvReader.Benchmark;

internal static class ZoneMapBenchmark
{
    private const int BlockSize = 1_024;
    private const int BlockCount = 256;
    private const int BandWidth = 10_000; // Cents
    private const int BuildSamples = 5;
    private const int LookupSamples = 15;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static void Run()
    {
        var random = new Random(431);
        var clustered = Generate(random);
        var shifted = new Order[clustered.Length];
        var shift = BlockSize / 3;
        clustered.AsSpan(shift).CopyTo(shifted);
        clustered.AsSpan(0, shift).CopyTo(shifted.AsSpan(clustered.Length - shift));
        var shuffled = (Order[])clustered.Clone();
        random.Shuffle(shuffled.AsSpan());

        var directory = Directory.CreateTempSubdirectory("pocket-zone-map-benchmark-").FullName;
        var scanPath = Path.Combine(directory, "scan.csv");
        var indexedPath = Path.Combine(directory, "indexed.csv");
        var sidecar = indexedPath + ".zonemap.idx";
        var narrow = new Query("narrow", 42 * BandWidth, 43 * BandWidth);

        Console.WriteLine($"{RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription} | {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine(FormattableString.Invariant(
            $"{clustered.Length:N0} rows, {BlockCount} blocks of {BlockSize:N0} rows, {LookupSamples} paired lookup samples, {BuildSamples} build samples."));
        Console.WriteLine("Layout,Query,Matches,CandidateBlocks,TotalBlocks,ScanMedianMs,ZoneMedianMs,ScanMedianBytes,ZoneMedianBytes,Speedup");

        try
        {
            RunLayout("clustered", clustered, scanPath, indexedPath, sidecar, [
                narrow,
                new("25% range", 42 * BandWidth, 106 * BandWidth),
                new("whole range", 0, BlockCount * BandWidth),
                new("outside range", BlockCount * BandWidth, (BlockCount + 1) * BandWidth)
            ]);
            RunLayout("shifted clusters", shifted, scanPath, indexedPath, sidecar, [narrow]);
            RunLayout("shuffled", shuffled, scanPath, indexedPath, sidecar, [narrow]);
        }
        finally
        {
            File.Delete(sidecar);
            File.Delete(scanPath);
            File.Delete(indexedPath);
            Directory.Delete(directory);
        }
    }

    private static Order[] Generate(Random random)
    {
        var bands = Enumerable.Range(0, BlockCount).ToArray();
        random.Shuffle(bands.AsSpan());
        var result = new Order[BlockCount * BlockSize];
        for (int block = 0; block < bands.Length; block++)
        {
            int band = bands[block];
            int offset = block * BlockSize;
            int firstId = band * BlockSize;
            int minimum = band * BandWidth;
            for (int row = 0; row < BlockSize; row++)
                result[offset + row] = new(firstId + row, minimum + random.Next(BandWidth));
        }
        return result;
    }

    private static void RunLayout(string layout, Order[] data, string scanPath, string indexedPath, string sidecar,
        Query[] queries)
    {
        File.Delete(sidecar);
        WriteOrders(scanPath, data);
        File.Copy(scanPath, indexedPath, overwrite: true);

        var builder = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithStreamInitialization(StreamInitializationOptions.Utf8);
        var sequential = builder.FromFiles(new[] { FileSource.Partitioned(scanPath, true) }).Build();
        var indexed = builder.FromFiles(new[] { FileSource.Partitioned(indexedPath, true) }).Build();
        var index = new ZoneMapIndex(new IndexKey<decimal>("Amount"), BlockSize);
        var optional = index.WithOptionalIndex();
        indexed.BuildIndexes(index);
        indexed.BuildIndexes(index);
        var builds = new double[BuildSamples];
        for (int sample = 0; sample < builds.Length; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            indexed.BuildIndexes(index);
            builds[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Console.WriteLine(FormattableString.Invariant(
            $"# {layout} | csv bytes {new FileInfo(indexedPath).Length} | sidecar bytes {new FileInfo(sidecar).Length} | build median {Median(builds):F3} ms"));

        foreach (var query in queries)
        {
            var range = new IndexRange<decimal>(query.Lower / 100m, query.Upper / 100m, includeUpper: false);
            var scanQuery = sequential.WithIndexedLookup(optional, range);
            var zoneQuery = indexed.WithIndexedLookup(index, range);
            int[] expected = data.Where(row => query.Contains(row.Amount)).Select(row => row.Id).ToArray();
            Result expectedResult = default;
            foreach (int id in expected)
                expectedResult = expectedResult.Add(id);

            AssertOrder(scanQuery, expected);
            AssertOrder(zoneQuery, expected);
            for (int i = 0; i < 5; i++)
            {
                Measure(scanQuery, expectedResult);
                Measure(zoneQuery, expectedResult);
            }

            var scan = new Sample[LookupSamples];
            var zone = new Sample[LookupSamples];
            for (int i = 0; i < LookupSamples; i++)
            {
                if (i % 2 == 0)
                {
                    scan[i] = Measure(scanQuery, expectedResult);
                    zone[i] = Measure(zoneQuery, expectedResult);
                }
                else
                {
                    zone[i] = Measure(zoneQuery, expectedResult);
                    scan[i] = Measure(scanQuery, expectedResult);
                }
            }

            double scanTime = Median(scan.Select(sample => sample.Milliseconds).ToArray());
            double zoneTime = Median(zone.Select(sample => sample.Milliseconds).ToArray());
            long scanBytes = Median(scan.Select(sample => sample.Bytes).ToArray());
            long zoneBytes = Median(zone.Select(sample => sample.Bytes).ToArray());
            int candidates = CountCandidateBlocks(data, query);
            Console.WriteLine(FormattableString.Invariant(
                $"{layout},{query.Name},{expected.Length},{candidates},{BlockCount},{scanTime:F3},{zoneTime:F3},{scanBytes},{zoneBytes},{scanTime / zoneTime:F3}"));
        }
    }

    private static int CountCandidateBlocks(Order[] data, Query query)
    {
        int candidates = 0;
        for (int offset = 0; offset < data.Length; offset += BlockSize)
        {
            int min = int.MaxValue, max = int.MinValue;
            foreach (var row in data.AsSpan(offset, BlockSize))
            {
                min = Math.Min(min, row.Amount);
                max = Math.Max(max, row.Amount);
            }
            if (max >= query.Lower && min < query.Upper)
                candidates++;
        }
        return candidates;
    }

    private static void WriteOrders(string path, Order[] data)
    {
        string[] regions = ["Brussels", "Ghent", "Antwerp", "Liège", "Bruges"];
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine("Id,Amount,Region,Channel,Notes");
        foreach (var order in data)
        {
            string notes = order.Id % 4096 == 0 ? "\"Call customer,\nthen confirm\""
                : order.Id % 17 == 0 ? "\"Café pickup, paid\"" : "Standard delivery";
            writer.WriteLine(string.Create(Culture,
                $"{order.Id},{order.Amount / 100m:F2},{regions[order.Id % regions.Length]},web,{notes}"));
        }
    }

    private static void AssertOrder(CsvFileQuery<bool> query, int[] expected)
    {
        using var rows = query.ToDataReader();
        foreach (int id in expected)
        {
            if (!rows.Read() || rows.GetInt32(0) != id)
                throw new InvalidOperationException($"Lookup disagreed with the expected order at ID {id}.");
        }
        if (rows.Read())
            throw new InvalidOperationException("Lookup returned extra rows.");
    }

    private static Sample Measure(CsvFileQuery<bool> query, Result expected)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        Result actual = default;
        using (var rows = query.ToDataReader())
        {
            while (rows.Read())
                actual = actual.Add(rows.GetInt32(0));
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (actual != expected)
            throw new InvalidOperationException($"Lookup returned {actual} instead of {expected}.");
        return new(elapsed, allocated);
    }

    private static T Median<T>(T[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private readonly record struct Order(int Id, int Amount);

    private readonly record struct Query(string Name, int Lower, int Upper)
    {
        public bool Contains(int amount) => amount >= Lower && amount < Upper;
    }

    private readonly record struct Result(int Count, ulong Fingerprint)
    {
        public Result Add(int id) => new(Count + 1, unchecked((Fingerprint ^ (uint)id) * 1099511628211UL));
    }

    private readonly record struct Sample(double Milliseconds, long Bytes);
}
