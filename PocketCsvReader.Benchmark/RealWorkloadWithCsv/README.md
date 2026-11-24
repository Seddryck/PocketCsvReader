# Real workload with CSV

The benchmark generates one deterministic, in-memory UTF-8 CSV with 50,000 data records and a header. Each invocation reads every record and converts university, first name, last name, and quarter to strings, score to a decimal, credits to a short, and registration date to a DateOnly. Dataset generation happens during setup, outside the measurement.

Run from the repository root:

```powershell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*RealWorkloadWithCsvBenchmark*'
```
