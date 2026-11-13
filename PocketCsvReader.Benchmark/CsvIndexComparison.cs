using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

namespace PocketCsvReader.Benchmark;

internal static class CsvIndexComparison
{
    private const int FileCount = 12;
    private const int DayCount = 4;
    private const int RowsPerDay = 4_096;
    private const int BlockSize = 512;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static void Run() => Run(new(FileCount, 1, 18, 3, 3));

    public static void Run100X() => Run(new(FileCount * 10, 10, 3, 1, 0));

    private static void Run(Settings settings)
    {
        var root = Directory.CreateTempSubdirectory("pocket-csv-index-comparison-").FullName;
        try
        {
            Console.WriteLine($"{RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription} | {RuntimeInformation.ProcessArchitecture}");
            var scanPaths = WriteSources(root, settings);
            long sourceBytes = scanPaths.Sum(path => new FileInfo(path).Length);
            int rowsPerFile = DayCount * RowsPerDay * settings.DayRepeats;
            Console.WriteLine(string.Create(Culture,
                $"{settings.Files * rowsPerFile:N0} rows in {settings.Files} files of {rowsPerFile:N0} rows | {sourceBytes:N0} CSV bytes | {settings.LookupSamples} lookup samples | {settings.BuildSamples} build samples"));
            Console.WriteLine("Mode,SidecarBytes,BuildMedianMs");
            var modes = PrepareModes(root, scanPaths, new Indexes(), settings.BuildSamples);

            Console.WriteLine("Workload,Mode,Matches,LookupMedianMs,LookupMedianAllocatedBytes,Speedup");
            RunWorkload(new("selective", "target", 3, 300, 400, 17, Broad: false), settings, modes);
            RunWorkload(new("broader", "shared", 3, 100, 900, 23, Broad: true), settings, modes);
            RunWorkload(new("absent-customer", "missing", 3, 300, 400, 17, Broad: false), settings, modes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Mode[] PrepareModes(string root, string[] scanPaths, Indexes indexes, int buildSamples)
    {
        var modes = new List<Mode>();
        var definitions = new (string Name, Sidecars Indexes)[]
        {
            ("Scan", Sidecars.None),
            ("Bloom", Sidecars.Bloom),
            ("Zone map", Sidecars.ZoneMap),
            ("Bitmap", Sidecars.Bitmap),
            ("Inverted", Sidecars.Inverted),
            ("Hash", Sidecars.Hash),
            ("Sparse", Sidecars.Sparse),
            ("Bloom + zone map + sparse", Sidecars.Bloom | Sidecars.ZoneMap | Sidecars.Sparse),
            ("All six", Sidecars.All)
        };
        foreach (var (name, enabled) in definitions)
        {
            var directory = Path.Combine(root, enabled.ToString());
            string[] paths = enabled == Sidecars.None ? scanPaths : CopySources(scanPaths, directory);
            var source = new CsvReaderBuilder()
                .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
                .WithStreamInitialization(StreamInitializationOptions.Utf8)
                .FromFiles(paths.Select(path => FileSource.Partitioned(path, true))).Build();
            double build = MeasureBuild(source, indexes, enabled, buildSamples);
            long sidecarBytes = Directory.EnumerateFiles(directory, "*.idx").Sum(path => new FileInfo(path).Length);
            Console.WriteLine(string.Create(Culture, $"{name},{sidecarBytes},{build:F3}"));
            modes.Add(new(name, workload => indexes.Lookup(source, enabled, workload)));
        }
        return modes.ToArray();
    }

    private static double MeasureBuild(CsvFileQuery<bool> source, Indexes indexes, Sidecars enabled, int buildSamples)
    {
        if (enabled == Sidecars.None)
            return 0;

        if (buildSamples > 1)
            indexes.Build(source, enabled);
        var samples = new double[buildSamples];
        for (int sample = 0; sample < samples.Length; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            indexes.Build(source, enabled);
            samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        return Median(samples);
    }

    private static void RunWorkload(Workload workload, Settings settings, Mode[] modes)
    {
        var expected = Enumerable.Range(0, settings.Files)
            .SelectMany(file => GenerateOrders(file, settings.DayRepeats))
            .Where(workload.Matches)
            .Select(order => order.Id)
            .ToArray();
        Console.Error.WriteLine($"Checking {workload.Name} result order in all configurations.");
        Result expectedResult = default;
        foreach (int id in expected)
            expectedResult = expectedResult.Add(id);

        var queries = modes.Select(mode => mode.Lookup(workload)).ToArray();
        var timings = queries.Select(_ => new Sample[settings.LookupSamples]).ToArray();
        foreach (var query in queries)
        {
            AssertOrder(query, expected);
            for (int i = 0; i < settings.WarmupSamples; i++)
                Measure(query, expectedResult);
        }

        // Spread the shorter run's starting positions evenly across the modes.
        int rotation = Math.Max(1, modes.Length / settings.LookupSamples);
        for (int sample = 0; sample < settings.LookupSamples; sample++)
        {
            Console.Error.WriteLine($"Measuring {workload.Name}, sample {sample + 1}/{settings.LookupSamples}.");
            for (int offset = 0; offset < modes.Length; offset++)
            {
                int index = ((sample * rotation) + offset) % modes.Length;
                timings[index][sample] = Measure(queries[index], expectedResult);
            }
        }

        double scanTime = MedianTime(timings[0]);
        for (int i = 0; i < modes.Length; i++)
        {
            double time = MedianTime(timings[i]);
            long bytes = Median(timings[i].Select(sample => sample.Bytes).ToArray());
            Console.WriteLine(string.Create(Culture,
                $"{workload.Name},{modes[i].Name},{expected.Length},{time:F3},{bytes},{scanTime / time:F3}"));
        }
    }

    private static void AssertOrder(CsvFileQuery<bool> query, int[] expected)
    {
        using var reader = query.ToDataReader();
        foreach (int id in expected)
        {
            if (!reader.Read() || reader.GetInt32(0) != id)
                throw new InvalidOperationException($"Lookup disagreed with source order at ID {id}.");
        }
        if (reader.Read())
            throw new InvalidOperationException("Lookup returned extra rows.");
    }

    private static Sample Measure(CsvFileQuery<bool> query, Result expected)
    {
        Result actual = default;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        using (var reader = query.ToDataReader())
        {
            while (reader.Read())
                actual = actual.Add(reader.GetInt32(0));
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (actual != expected)
            throw new InvalidOperationException($"Lookup returned {actual} instead of {expected}.");
        return new(elapsed, bytes);
    }

    private static IEnumerable<Order> GenerateOrders(int file, int dayRepeats)
    {
        int layout = file % FileCount;
        var localCustomers = Enumerable.Range(0, 503)
            .Select(customer => string.Create(Culture, $"local-{layout:D2}-{customer:D3}")).ToArray();
        int rowsPerDay = RowsPerDay * dayRepeats;
        int id = file * DayCount * rowsPerDay;
        for (int day = 1; day <= DayCount; day++)
        {
            for (int position = 0; position < rowsPerDay; position++)
            {
                int row = position % RowsPerDay;
                string customer = row % 4 != 0 ? "shared"
                    : layout is 2 or 9 ? "target" : localCustomers[row % localCustomers.Length];
                string status = ((row / 4) % 4) switch { 0 => "pending", 1 => "delivered", _ => "shipped" };
                string region = (row / 16) % 4 == 3 ? "US" : "EU";
                string tags = ((row / 64) % 4) switch { 0 => "urgent|refund", 1 => "urgent|delivery", _ => "routine" };
                int amount = 100 + (100 * (row / BlockSize)) + (row % 100);
                int warehouse = (row / 8) % 7 == 0 ? 17 : 23;
                yield return new(id++, day, customer, amount, status, region, tags, warehouse);
            }
        }
    }

    private static string[] WriteSources(string root, Settings settings)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, Sidecars.None.ToString())).FullName;
        var paths = new string[settings.Files];
        for (int file = 0; file < paths.Length; file++)
        {
            paths[file] = Path.Combine(directory, string.Create(Culture, $"orders-{file:D2}.csv"));
            using var writer = new StreamWriter(paths[file], append: false, new UTF8Encoding(false)) { NewLine = "\n" };
            writer.WriteLine("Id,Day,Customer,Amount,Status,Region,Tags,Warehouse,Notes");
            foreach (var row in GenerateOrders(file, settings.DayRepeats))
            {
                string notes = row.Id % 4_096 == 0 ? "\"Call customer,\nthen confirm\"" : "Standard delivery";
                writer.WriteLine(string.Create(Culture,
                    $"{row.Id},{row.Day},{row.Customer},{row.Amount},{row.Status},{row.Region},{row.Tags},{row.Warehouse},{notes}"));
            }
        }
        return paths;
    }

    private static string[] CopySources(string[] sources, string directory)
    {
        Directory.CreateDirectory(directory);
        return sources.Select(source =>
        {
            var destination = Path.Combine(directory, Path.GetFileName(source));
            File.Copy(source, destination);
            return destination;
        }).ToArray();
    }

    private static double MedianTime(Sample[] samples) => Median(samples.Select(sample => sample.Milliseconds).ToArray());

    private static T Median<T>(T[] values)
        where T : INumber<T>
    {
        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / T.CreateChecked(2) : values[middle];
    }

    [Flags]
    private enum Sidecars
    {
        None = 0,
        Bloom = 1,
        ZoneMap = 2,
        Bitmap = 4,
        Inverted = 8,
        Hash = 16,
        Sparse = 32,
        All = Bloom | ZoneMap | Bitmap | Inverted | Hash | Sparse
    }

    private sealed record Settings(int Files, int DayRepeats, int LookupSamples, int BuildSamples, int WarmupSamples);

    private sealed record Mode(string Name, Func<Workload, CsvFileQuery<bool>> Lookup);

    private readonly record struct Order(int Id, int Day, string Customer, int Amount, string Status, string Region,
        string Tags, int Warehouse);

    private sealed record Workload(string Name, string Customer, int Day, int Minimum, int Maximum, int Warehouse, bool Broad)
    {
        public bool Matches(Order order)
            => order.Day == Day && order.Customer == Customer && order.Amount >= Minimum && order.Amount < Maximum
                && (Broad || order.Status is "delivered" or "shipped") && order.Region == "EU"
                && (Broad || order.Tags == "urgent|refund") && order.Warehouse == Warehouse;
    }

    private readonly record struct Sample(double Milliseconds, long Bytes);

    private readonly record struct Result(int Count, ulong Fingerprint)
    {
        public Result Add(int id) => new(Count + 1, unchecked((Fingerprint ^ (uint)id) * 1099511628211UL));
    }

    private sealed class Indexes
    {
        private readonly BloomIndex _bloom = new(new IndexKey<string>("Customer"), expectedDistinctCount: 600);
        private readonly ZoneMapIndex _zone = new(new IndexKey<int>("Amount"), BlockSize);
        private readonly BitmapIndex _bitmap = new(new IndexKey<string>("Status"), new IndexKey<string>("Region"));
        private readonly InvertedIndex _inverted = new(new IndexKey<string>("Tags"), value => value.Split('|'));
        private readonly HashIndex _hash = new(new IndexKey<int>("Warehouse"));
        private readonly SparseIndex _sparse = new(new IndexKey<int>("Day"), BlockSize);

        public void Build(CsvFileQuery<bool> source, Sidecars enabled)
        {
            if (enabled.HasFlag(Sidecars.Bloom))
                source.BuildIndexes(_bloom);
            if (enabled.HasFlag(Sidecars.ZoneMap))
                source.BuildIndexes(_zone);
            if (enabled.HasFlag(Sidecars.Bitmap))
                source.BuildIndexes(_bitmap);
            if (enabled.HasFlag(Sidecars.Inverted))
                source.BuildIndexes(_inverted);
            if (enabled.HasFlag(Sidecars.Hash))
                source.BuildIndexes(_hash);
            if (enabled.HasFlag(Sidecars.Sparse))
                source.BuildIndexes(_sparse);
        }

        public CsvFileQuery<bool> Lookup(CsvFileQuery<bool> source, Sidecars enabled, Workload workload)
        {
            var range = new IndexRange<int>(workload.Minimum, workload.Maximum, includeUpper: false);
            var category = BitmapPredicate.Equal("Region", "EU").And(workload.Broad
                ? BitmapPredicate.In("Status", "shipped", "delivered", "pending")
                : BitmapPredicate.In("Status", "shipped", "delivered"));
            var tags = workload.Broad ? TermPredicate.ContainsAny("urgent", "routine") : TermPredicate.ContainsAll("urgent", "refund");
            return source
                .WithIndexedLookup(enabled.HasFlag(Sidecars.Bloom) ? _bloom : _bloom.WithOptionalIndex(), workload.Customer)
                .WithIndexedLookup(enabled.HasFlag(Sidecars.ZoneMap) ? _zone : _zone.WithOptionalIndex(), range)
                .WithIndexedLookup(enabled.HasFlag(Sidecars.Bitmap) ? _bitmap : _bitmap.WithOptionalIndex(), category)
                .WithIndexedLookup(enabled.HasFlag(Sidecars.Inverted) ? _inverted : _inverted.WithOptionalIndex(), tags)
                .WithIndexedLookup(enabled.HasFlag(Sidecars.Hash) ? _hash : _hash.WithOptionalIndex(), workload.Warehouse)
                .WithIndexedLookup(enabled.HasFlag(Sidecars.Sparse) ? _sparse : _sparse.WithOptionalIndex(), workload.Day);
        }
    }
}
