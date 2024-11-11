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
            var reader = csvReader.ToDataReader(stream);
            while (reader.Read())
            {
                // Do nothing
            }
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
