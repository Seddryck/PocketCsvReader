using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Apache.Arrow;
using PocketCsvReader.Arrow;
using PocketCsvReader.Arrow.ColumnarJson;
using PocketCsvReader.Arrow.NetCdf;

namespace PocketCsvReader.Benchmark;

internal static class ArrowBatchTiming
{
    private const int X = 16;
    private const int Y = 64;
    private const int Z = 256;
    private const int RowCount = X * Y * Z;
    private const int BatchSize = 4096;
    private const int BatchCount = RowCount / BatchSize;
    private const int SampleCount = 11;
    private const int WarmupCount = 2;

    private static readonly string[] Names = ["x", "y", "z", "value"];

    public static async Task RunMatrixAsync()
    {
        var directory = Directory.CreateTempSubdirectory("pocketcsvreader-arrow-benchmark-");
        try
        {
            var flat = Path.Combine(directory.FullName, "flat.cdl");
            var grid = Path.Combine(directory.FullName, "grid.cdl");
            var integration = Path.Combine(directory.FullName, "integration.json");
            WriteCdl(flat, grid: false);
            WriteCdl(grid, grid: true);
            WriteIntegrationJson(integration);

            var dense = new NetCdfCdlReader(new() { BatchSize = BatchSize });
            var encoded = new NetCdfCdlReader(new() { BatchSize = BatchSize, RunEndEncodeCoordinates = true });
            Scenario[] scenarios =
            [
                new("Flat CDL", flat, dense),
                new("Gridded CDL", grid, dense),
                new("Gridded CDL with REE", grid, encoded),
                new("Arrow Integration JSON", integration, new ArrowIntegrationJsonReader()),
            ];
            foreach (var scenario in scenarios)
                await ReadAsync(scenario, asynchronous: false, validateValues: true).ConfigureAwait(false);

            Console.Error.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
            Console.Error.WriteLine($"{RowCount} verified rows, {BatchCount} batches of {BatchSize}, {WarmupCount} warmups and {SampleCount} measured runs per case.");
            Console.WriteLine("Mode,Reader,FirstReadMs,FirstReadyMs,SecondReadMs,SecondReadyMs,Batch64ReadyMs,FullReadMs");
            var random = new Random(90721);
            await MeasureAsync(scenarios, random, asynchronous: false).ConfigureAwait(false);
            await MeasureAsync(scenarios, random, asynchronous: true).ConfigureAwait(false);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task MeasureAsync(Scenario[] scenarios, Random random, bool asynchronous)
    {
        foreach (var scenario in scenarios)
        {
            for (var run = 0; run < WarmupCount; run++)
                await ReadAsync(scenario, asynchronous).ConfigureAwait(false);
        }

        var order = scenarios.ToArray();
        var samples = scenarios.ToDictionary(scenario => scenario, _ => new Sample[SampleCount]);
        for (var run = 0; run < SampleCount; run++)
        {
            random.Shuffle(order);
            foreach (var scenario in order)
                samples[scenario][run] = await ReadAsync(scenario, asynchronous).ConfigureAwait(false);
        }

        var mode = asynchronous ? "Async" : "Sync";
        foreach (var scenario in scenarios)
        {
            var result = samples[scenario];
            Console.WriteLine(FormattableString.Invariant(
                $"{mode},{scenario.Name},{Median(result, x => x.FirstRead):F3},{Median(result, x => x.FirstReady):F3},{Median(result, x => x.SecondRead):F3},{Median(result, x => x.SecondReady):F3},{Median(result, x => x.LastReady):F3},{Median(result, x => x.Total):F3}"));
        }
    }

    private static double Median(Sample[] samples, Func<Sample, double> select)
        => samples.Select(select).Order().ElementAt(samples.Length / 2);

    private static async Task<Sample> ReadAsync(Scenario scenario, bool asynchronous, bool validateValues = false)
    {
        var start = Stopwatch.GetTimestamp();
        var first = 0L;
        var firstRead = 0L;
        var second = 0L;
        var secondRead = 0L;
        var last = 0L;
        var batches = 0;
        using (var stream = asynchronous
            ? await scenario.Reader.ToArrowStreamAsync(scenario.Path).ConfigureAwait(false)
            : scenario.Reader.ToArrowStream(scenario.Path))
        {
            while (true)
            {
                var readStarted = batches < 2 ? Stopwatch.GetTimestamp() : 0L;
                var batch = await stream.ReadNextRecordBatchAsync().ConfigureAwait(false);
                if (batch is null)
                    break;

                using (batch)
                {
                    switch (batches)
                    {
                        case 0:
                            first = Stopwatch.GetTimestamp();
                            firstRead = first - readStarted;
                            break;
                        case 1:
                            second = Stopwatch.GetTimestamp();
                            secondRead = second - readStarted;
                            break;
                        case BatchCount - 1:
                            last = Stopwatch.GetTimestamp();
                            break;
                    }

                    CheckBatch(batch, batches++, scenario, validateValues);
                }
            }
        }

        var end = Stopwatch.GetTimestamp();
        if (batches != BatchCount)
            throw new InvalidDataException($"{scenario.Name} yielded {batches} batches, expected {BatchCount}.");

        return new Sample(
            Stopwatch.GetElapsedTime(0, firstRead).TotalMilliseconds,
            Stopwatch.GetElapsedTime(start, first).TotalMilliseconds,
            Stopwatch.GetElapsedTime(0, secondRead).TotalMilliseconds,
            Stopwatch.GetElapsedTime(start, second).TotalMilliseconds,
            Stopwatch.GetElapsedTime(start, last).TotalMilliseconds,
            Stopwatch.GetElapsedTime(start, end).TotalMilliseconds);
    }

    private static void CheckBatch(RecordBatch batch, int index, Scenario scenario, bool validateValues)
    {
        if (batch.Length != BatchSize)
            throw new InvalidDataException($"{scenario.Name} returned {batch.Length} rows in batch {index + 1}.");
        if (!validateValues)
            return;

        var x = batch.Column(0);
        var y = batch.Column(1);
        var z = batch.Column(2);
        var values = (FloatArray)batch.Column(3);
        for (var local = 0; local < batch.Length; local++)
        {
            var row = (index * BatchSize) + local;
            if (ReadCoordinate(x, local) != Coordinate(0, row)
                || ReadCoordinate(y, local) != Coordinate(1, row)
                || ReadCoordinate(z, local) != Coordinate(2, row)
                || values.GetValue(local) != Value(row))
                throw new InvalidDataException($"{scenario.Name} produced unexpected data at row {row}.");
        }
    }

    private static int? ReadCoordinate(IArrowArray array, int index)
    {
        if (array is Int32Array dense)
            return dense.GetValue(index);

        var encoded = (RunEndEncodedArray)array;
        var ends = ((Int32Array)encoded.RunEnds).Values;
        var run = ends.BinarySearch(index + 1);
        return ((Int32Array)encoded.Values).GetValue(run < 0 ? ~run : run);
    }

    private static int Coordinate(int column, int row) => column switch
    {
        0 => 1000 + (row / (Y * Z)),
        1 => 2000 + ((row / Z) % Y),
        2 => 3000 + (row % Z),
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    private static float Value(int row) => ((row % 100000) * 0.25f) + 0.125f;

    private static void WriteCdl(string path, bool grid)
    {
        using var writer = File.CreateText(path);
        writer.WriteLine(grid
            ? FormattableString.Invariant($"netcdf grid {{ dimensions: x={X}, y={Y}, z={Z}; variables: int x(x), y(y), z(z); float value(x,y,z); data:")
            : FormattableString.Invariant($"netcdf flat {{ dimensions: x={RowCount}; variables: int x(x), y(x), z(x); float value(x); data:"));
        int[] lengths = [X, Y, Z, RowCount];
        int[] strides = [Y * Z, Z, 1, 1];
        for (var column = 0; column < Names.Length; column++)
        {
            writer.Write(Names[column]);
            writer.Write('=');
            var count = grid ? lengths[column] : RowCount;
            var stride = grid ? strides[column] : 1;
            for (var cell = 0; cell < count; cell++)
            {
                if (cell > 0)
                    writer.Write(',');
                var row = cell * stride;
                writer.Write(column == 3
                    ? Value(row).ToString("R", CultureInfo.InvariantCulture)
                    : Coordinate(column, row).ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteLine(";");
        }

        writer.WriteLine("}");
    }

    private static void WriteIntegrationJson(string path)
    {
        using var file = File.Create(path);
        using var writer = new Utf8JsonWriter(file);
        writer.WriteStartObject();
        writer.WriteStartObject("schema");
        writer.WriteStartArray("fields");
        for (var column = 0; column < Names.Length; column++)
            WriteField(writer, column);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("batches");
        for (var start = 0; start < RowCount; start += BatchSize)
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", BatchSize);
            writer.WriteStartArray("columns");
            for (var column = 0; column < Names.Length; column++)
                WriteColumn(writer, column, start);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteField(Utf8JsonWriter writer, int column)
    {
        writer.WriteStartObject();
        writer.WriteString("name", Names[column]);
        writer.WriteBoolean("nullable", true);
        writer.WriteStartObject("type");
        if (column == 3)
        {
            writer.WriteString("name", "floatingpoint");
            writer.WriteString("precision", "SINGLE");
        }
        else
        {
            writer.WriteString("name", "int");
            writer.WriteBoolean("isSigned", true);
            writer.WriteNumber("bitWidth", 32);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteColumn(Utf8JsonWriter writer, int column, int start)
    {
        writer.WriteStartObject();
        writer.WriteString("name", Names[column]);
        writer.WriteNumber("count", BatchSize);
        writer.WriteStartArray("VALIDITY");
        for (var offset = 0; offset < BatchSize; offset++)
            writer.WriteNumberValue(1);
        writer.WriteEndArray();
        writer.WriteStartArray("DATA");
        for (var row = start; row < start + BatchSize; row++)
        {
            if (column == 3)
                writer.WriteNumberValue(Value(row));
            else
                writer.WriteNumberValue(Coordinate(column, row));
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private sealed record Scenario(string Name, string Path, IArrowStreamSource Reader);

    private readonly record struct Sample(
        double FirstRead, double FirstReady, double SecondRead, double SecondReady, double LastReady, double Total);
}
