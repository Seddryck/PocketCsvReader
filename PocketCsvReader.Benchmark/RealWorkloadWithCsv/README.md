# Real workload with CSV

Each case uses one deterministic, in-memory CSV with a header and 50,000 records. The same records are encoded as UTF-8 without a byte order mark or as little-endian UTF-16 with a byte order mark. Each benchmark opens a fresh stream over the same bytes and reads every field. The university, first name, last name, and quarter are strings. The score is a decimal, the credits are a short, and the registration date is a DateOnly.

The comparison uses PocketCsvReader as the baseline and also runs Sep, Sylvan.Data.Csv, CsvHelper, SoftCircuits.CsvParser, and TinyCsvParser. SoftCircuits returns strings, so the benchmark converts its score, credits, and date. TinyCsvParser maps each row to a typed object, so its measured allocations include that object.

Dataset generation and verification of all seven values in every row happen during setup, outside the measured runs. BenchmarkDotNet reports elapsed time and managed allocations per pass through the data.

This comparison targets .NET 10. Run it from the repository root:

```powershell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*RealWorkloadWithCsvBenchmark*'
```
