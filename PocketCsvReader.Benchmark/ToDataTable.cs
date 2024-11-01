using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Bogus;

namespace PocketCsvReader.Benchmark;

[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
public class ToDataTable
{
    private readonly string _filePath1MB = "data/1MBFile.csv";

    //[Params(16_300, 163_000, 1_630_000)]
    [Params(16_300, 163_000)]
    //[Params(16_300)]
    public int recordCount;

    [GlobalSetup]
    public void Setup()
    {
        if (!Directory.Exists("data"))
            Directory.CreateDirectory("data");

        var faker = new Faker<CustomerRecord>()
            .RuleFor(p => p.Firstname, f => f.Name.FirstName())
            .RuleFor(p => p.Lastname, f => f.Name.LastName())
            .RuleFor(p => p.Gender, f => f.PickRandom(new[] { "Male", "Female" }))
            .RuleFor(p => p.DateOfBirth, f => f.Date.Past(50, DateTime.Now.AddYears(-18)))
            .RuleFor(p => p.Year, f => f.Date.Recent(365).Year) 
            .RuleFor(p => p.Month, f => f.Date.Month().ToString(CultureInfo.CurrentCulture))
            .RuleFor(p => p.TotalOrder, f => f.Finance.Amount(50, 500)); 

        // Generate the list of records
        var records = faker.Generate(recordCount);

        // Write the data to CSV
        using (var writer = new StreamWriter(_filePath1MB))
        {
            foreach (var record in records)
                writer.WriteLine($"{record.Firstname},{record.Lastname},{record.Gender},{record.DateOfBirth},{record.Year},{record.Month},{record.TotalOrder}");
        }

        Console.WriteLine($"CSV file generated at: {_filePath1MB}");
        Console.WriteLine($"CSV file generated with size: {new FileInfo(_filePath1MB).Length}");
    }

    [Benchmark]
    public void Read1MBFile()
    {
        MeasureMemory(() => ReadFile(_filePath1MB));
    }

    private void ReadFile(string filePath)
    {
        using (var stream = new FileStream(filePath, FileMode.Open))
        {
            // Assume PocketCsvReader takes a StreamReader or stream as input
            var csvReader = new CsvReader(new CsvProfile(',', '\"', "\r\n", false));
            csvReader.ToDataTable(stream);
        }
    }

    private void MeasureMemory(Action action)
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

        Console.WriteLine($"Memory Used (GC): {memoryAfter - memoryBefore} bytes");
        Console.WriteLine($"Memory Used (Working Set): {workingSetAfter - workingSetBefore} bytes");
    }
}
