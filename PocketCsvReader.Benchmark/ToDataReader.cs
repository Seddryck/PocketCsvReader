using System;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using Bogus;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[Config(typeof(CustomConfig))]
public class ToDataReader
{
    private readonly string _filePath = "data/1MBFile.csv";

    [ParamsSource(nameof(Versions))]
    public string VersionPath { get; set; } = string.Empty;

    public static IEnumerable<string> Versions => Directory.GetDirectories(@"C:\Users\cedri\Projects\PocketCsvReader\NuGetPackages\");

    private Assembly? _csvAssembly;

    //[Params(16_300, 163_000, 1_630_000)]
    //[Params(16_300, 163_000)]
    //[Params(16_300)]
    [Params(1_630_000)]
    public int recordCount;

    [GlobalSetup]
    public void Setup()
    {
        if (!Directory.Exists("data"))
            Directory.CreateDirectory("data");

        var faker = new Faker<CustomerRecord>()
            .CustomInstantiator(static f => new CustomerRecord(
                f.Name.FirstName(),
                f.Name.LastName(),
                f.PickRandom(new[] { "Male", "Female" }),
                f.Date.Past(50, DateTime.Now.AddYears(-18)),
                f.Date.Recent(365).Year,
                f.Date.Month().ToString(CultureInfo.CurrentCulture),
                f.Finance.Amount(50, 500)
            ))
            .RuleFor(p => p.Year, (f, p) => p.DateOfBirth.Year)
            .RuleFor(p => p.Month, (f, p) => p.DateOfBirth.ToString("MMMM", CultureInfo.CurrentCulture));


        // Generate the list of records
        var records = faker.Generate(recordCount);

        // Write the data to CSV
        using (var writer = new StreamWriter(_filePath))
        {
            foreach (var record in records)
                writer.WriteLine($"{record.Firstname},{record.Lastname},{record.Gender},{record.DateOfBirth},{record.Year},{record.Month},{record.TotalOrder}");
        }

        Console.WriteLine($"CSV file generated at: {_filePath}");
        Console.WriteLine($"CSV file generated with size: {new FileInfo(_filePath).Length}");
    }

    [Benchmark]
    public void ReadCsvFile()
    {
        _csvAssembly = LoadPocketCsvReader(VersionPath);
        ReadFile(_filePath, _csvAssembly!);
    }


    private void LogResults(string version, long memoryUsed, long workingSetUsed)
    {
        string logFile = "BenchmarkResults.txt";
        File.AppendAllText(logFile, $"Version: {version}, Memory Used (GC): {memoryUsed}, Memory Used (Working Set): {workingSetUsed}{Environment.NewLine}");
    }

    private void ReadFile(string filePath, Assembly csvAssembly)
    {
        var csvReaderType = csvAssembly.GetType("PocketCsvReader.CsvReader")!;
        var csvProfileType = csvAssembly.GetType("PocketCsvReader.CsvProfile")!;

        var csvProfile = Activator.CreateInstance(csvProfileType, ',', '\"', "\r\n", false);
        dynamic csvReader = Activator.CreateInstance(csvReaderType, csvProfile)!;
        
        using (var stream = new FileStream(filePath, FileMode.Open))
        {
            using var reader = csvReader.ToDataReader(stream);
            while (reader.Read())
            {
                // Do nothing
            }
        }
    }

    private void MeasureMemory(Action action, string version)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memoryBefore = GC.GetTotalMemory(true);
        var process = Process.GetCurrentProcess();
        long workingSetBefore = process.WorkingSet64;

        action(); // Run the CSV read action

        process.Refresh();
        long workingSetAfter = process.WorkingSet64;
        long memoryAfter = GC.GetTotalMemory(false);

        long gcMemoryUsed = memoryAfter - memoryBefore;
        long workingSetMemoryUsed = workingSetAfter - workingSetBefore;

        Console.WriteLine($"Memory Used (GC): {gcMemoryUsed} bytes");
        Console.WriteLine($"Memory Used (Working Set): {workingSetMemoryUsed} bytes");

        LogResults(version, gcMemoryUsed, workingSetMemoryUsed);
    }

    private Assembly LoadPocketCsvReader(string versionPath)
    {
        string dllPath = Path.Combine(versionPath, "PocketCsvReader.dll");
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"DLL not found: {dllPath}");
        }

        // Use a custom AssemblyLoadContext to load the assembly
        var context = new AssemblyLoadContext("PocketCsvReaderContext", isCollectible: true);
        return context.LoadFromAssemblyPath(dllPath);
    }

    private class CustomConfig : ManualConfig
    {
        public CustomConfig()
        {
            foreach (var versionPath in ToDataReader.Versions)
            {
                var versionName = Path.GetFileName(versionPath);

                // Create a specific job for each version
                AddJob(Job.Default
                    .WithRuntime(CoreRuntime.Core80)
                    .WithWarmupCount(1)    // 1 warm-up iteration
                    .WithIterationCount(5) // 5 actual iterations
                ); // Identify the job by version
            }
        }
    }
}
