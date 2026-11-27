using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PocketCsvReader.Arrow;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

internal static class ArrowUtf8Strings
{
    private const int RowCount = 100_000;
    private const int BatchSize = 4_096;
    private const int WarmupCount = 5;
    private const int SampleCount = 11;

    public static void Run()
    {
        var text = CreateInput(mixed: false);
        var mixed = CreateInput(mixed: true);
        Scenario[] scenarios =
        [
            new("Text-heavy UTF-8", text, CreateReader(StreamInitializationOptions.Utf8)),
            new("Mixed UTF-8", mixed, CreateReader(StreamInitializationOptions.Utf8, mixed: true)),
            new("Mixed decoded characters", mixed, CreateReader(new(), mixed: true)),
            new("Text-heavy UTF-8 with custom parsers", text, CreateReader(StreamInitializationOptions.Utf8, transform: true)),
        ];

        Console.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"{RowCount:N0} rows, batch size {BatchSize:N0}, {WarmupCount} warmups and {SampleCount} samples per case");
        for (int run = 0; run < WarmupCount; run++)
        {
            foreach (var scenario in scenarios)
                Measure(scenario);
        }

        var samples = scenarios.ToDictionary(scenario => scenario, _ => new Sample[SampleCount]);
        for (int run = 0; run < SampleCount; run++)
        {
            for (int index = 0; index < scenarios.Length; index++)
            {
                var scenario = scenarios[(run + index) % scenarios.Length];
                samples[scenario][run] = Measure(scenario);
            }
        }

        Console.WriteLine("Scenario,Rows/s,MiB/s,Bytes/run");
        foreach (var scenario in scenarios)
        {
            var seconds = samples[scenario].Select(sample => sample.Seconds).Order().ElementAt(SampleCount / 2);
            var bytes = samples[scenario].Select(sample => sample.Bytes).Order().ElementAt(SampleCount / 2);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{scenario.Name},{RowCount / seconds:F0},{scenario.Input.Length / seconds / (1024 * 1024):F1},{bytes}"));
        }
    }

    private static Sample Measure(Scenario scenario)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        int rows = 0;
        using var stream = new MemoryStream(scenario.Input, writable: false);
        foreach (var batch in scenario.Reader.ToArrowBatches(stream, BatchSize))
        {
            using (batch)
                rows += batch.Length;
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (rows != RowCount)
            throw new InvalidDataException($"Expected {RowCount} rows, received {rows}.");
        return new Sample(elapsed.TotalSeconds, bytes);
    }

    private static CsvReader CreateReader(StreamInitializationOptions initialization, bool mixed = false, bool transform = false)
    {
        var schema = new SchemaDescriptor.NamedSchemaDescriptor();
        ParseFunction? parser = transform ? static value => value.ToUpperInvariant() : null;
        schema.Fields.Add(new FieldDescriptor(mixed ? typeof(int) : typeof(string), "id", Parse: parser));
        schema.Fields.Add(new FieldDescriptor(typeof(string), "name", Parse: parser));
        schema.Fields.Add(new FieldDescriptor(typeof(string), "description", Parse: parser));
        schema.Fields.Add(new FieldDescriptor(mixed ? typeof(double) : typeof(string), "value", Parse: parser));
        return new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(schema)
            .WithStreamInitialization(initialization)
            .Build();
    }

    private static byte[] CreateInput(bool mixed)
    {
        var text = new StringBuilder("id,name,description,value\n");
        for (int i = 0; i < RowCount; i++)
        {
            var name = i % 4 == 0 ? "Café 東京 😀" : "Product";
            var description = i % 16 == 0 ? "\"Quoted \"\"product\"\" description\"" : "Ordinary product description";
            var id = i.ToString(CultureInfo.InvariantCulture);
            string value = mixed ? id + ".25" : "Category " + id;
            text.Append(CultureInfo.InvariantCulture, $"{id},{name} {id},{description},{value}\n");
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private sealed record Scenario(string Name, byte[] Input, CsvReader Reader);
    private readonly record struct Sample(double Seconds, long Bytes);
}
