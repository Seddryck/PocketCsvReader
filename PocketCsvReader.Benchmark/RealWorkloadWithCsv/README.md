# Real workload with CSV

Each case uses one deterministic, in-memory CSV with a header and 50,000 records. Seeded Bogus data supplies exactly 3,500 distinct first-and-last-name pairs. Every pair appears, with repeated pairs shuffled throughout the fixture. The first two scenarios encode the same records as UTF-8 without a byte order mark and as little-endian UTF-16 with a byte order mark. The third uses UTF-8 and changes University of California, Berkeley to University of California, "Berkeley". The CSV writes that field as `"University of California, ""Berkeley"""`. The fourth uses the original UTF-8 records and joins the parsed first and last names with one space per row. The fifth uses the original UTF-8 records and parses the quarter as a Chrononuensis YearQuarter. Each benchmark opens a fresh stream over the same bytes and reads every field. The university, first name, and last name are strings. The first four scenarios also read the quarter as a string. The score is a decimal, the credits are a short, and the registration date is a DateOnly.

The comparison uses PocketCsvReader as the baseline and also runs Sep, Sylvan.Data.Csv, CsvHelper, SoftCircuits.CsvParser, and TinyCsvParser. SoftCircuits returns strings, so the benchmark converts its score, credits, and date. TinyCsvParser maps each row to a typed object, so its measured allocations include that object.

PocketCsvReader uses a buffer size of 65 × 1024, disables read-ahead, and enables per-reader string pooling. It uses the native UTF-8 pool for the UTF-8 scenarios and a character string pool for UTF-16. Escape decoding is enabled only in the quoted-university scenario. CsvHelper enables per-column string pooling in every scenario.

Setup generates the dataset and verifies all seven field values in every row. It also verifies the joined name in the fourth scenario. BenchmarkDotNet reports elapsed time and managed allocations per pass through the data.

This comparison targets .NET 10. Run it from the repository root:

```powershell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*RealWorkloadWithCsvBenchmark*'
```
