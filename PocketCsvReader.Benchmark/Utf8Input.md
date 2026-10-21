# UTF-8 input comparison

Run the two parsing representations against the same in-memory input:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*Utf8Input*' --warmupCount 3 --iterationCount 5 --launchCount 1
```

Each operation reads 2,000 records. Narrow records contain three fields; wide records contain 24. Numeric access calls `GetInt32` on every field, string access calls `GetString` on every field, and skipped-column cases advance through the records without requesting values. Dataset creation is outside measurement. MemoryDiagnoser reports managed allocations per complete operation. These measurements cover parsing and access, not disk throughput.

Sample measured on 2026-10-22 with BenchmarkDotNet 0.15.8, .NET 10.0.12, Windows 11 x64, and Intel Core Ultra 9 275HX:

| Scenario | Character mean | UTF-8 byte mean | Character allocated | UTF-8 byte allocated |
| --- | ---: | ---: | ---: | ---: |
| ASCII numeric, narrow | 597.3 μs | 411.9 μs | 608.52 KB | 534.80 KB |
| ASCII, wide, skipped | 2,274.1 μs | 2,183.4 μs | 3,425.15 KB | 2,836.46 KB |
| ASCII strings, wide | 3,003.0 μs | 3,290.0 μs | 4,935.84 KB | 6,269.33 KB |
| Multiline strings, narrow | 671.9 μs | 710.8 μs | 997.60 KB | 1,192.43 KB |
| Unicode strings, narrow | 460.9 μs | 727.4 μs | 796.12 KB | 1,004.91 KB |
| Unicode, wide, skipped | 2,608.4 μs | 3,357.4 μs | 3,425.22 KB | 2,836.46 KB |

The numeric case took about 31% less time and allocated about 12% less memory on this machine. Skipped wide rows allocated about 17% less, although scanning multibyte text was slower. Full string materialization allocated more and was slower on the byte path. This is a short local comparison with one launch and five measured iterations; small differences need longer runs, and results do not establish a universal speedup. Character parsing remains the default. Benchmark the intended workload before choosing an input mode.

The implementation uses pooled contiguous buffers. No pipeline or external UTF-8 line-reader prototype was introduced, so these results compare only the two shipped parsers.
