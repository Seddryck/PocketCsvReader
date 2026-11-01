# Web-log parser comparison: 50,000 records

Each operation reads 50,000 deterministic synthetic web-log records and projects
each row to `(string Request, int Status, long Bytes)`. The value tuple is consumed
immediately into a checksum; results are streamed rather than retained in a list.
Setup verifies every projected value against the generator, checks the exact row
count, and runs outside the timed region.

The common payload contains host, identity, user, timestamp, request, status, and
response bytes. CSV, fixed-width, JSON, NDJSON, logfmt, LTSV, and Common Log encode
these seven fields. Combined Log additionally contains referrer and user agent.
W3C uses its standard separate date, time, method, and URI fields; projection
reconstructs `GET <URI> HTTP/1.1` and includes that allocation and work. These are
equivalent projected outputs, not identical input bytes or identical parsing work.
The data varies hosts, users, request paths, status codes, and byte counts, with
no missing projected values. It does not cover arbitrary malformed input,
Unicode, complex escaping, or deeply nested JSON.

All inputs are pre-encoded UTF-8 without BOM, using LF newlines, and read through
fresh read-only memory streams. Data generation, encoding, and reader configuration
are excluded; stream/data-reader creation, parsing, typed access, three-value
projection, checksum consumption, and disposal are included. All parsers use 4 KiB
buffers, no read-ahead, no initial row counting, and no position tracking. CSV
explicitly disables encoding probing. JSON/NDJSON use ordinary parsing without
optional stable-shape or ordered-projection shortcuts.

## Reproduce

From the repository root:

```powershell
dotnet build PocketCsvReader.Benchmark/PocketCsvReader.Benchmark.csproj -c Release -f net10.0
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --validate-web-logs
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --web-log-matrix
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --filter '*WebLogParserComparison*'
```

The matrix measures elapsed time with `Stopwatch`, process CPU time with
`Process.TotalProcessorTime`, and managed allocations with
`GC.GetAllocatedBytesForCurrentThread`. Each parser warms up for at least two
seconds, followed by seven samples lasting at least one second each. A full GC
runs before each sample and is excluded from the measurements. CPU time includes
process threads (including GC); allocations count the synchronous calling thread.
The matrix reports sample mean elapsed time, sample standard deviation, mean CPU
time, and mean allocated bytes per full input. Long batches reduce CPU timer
quantization error. It checks the checksum on every measured invocation.

BenchmarkDotNet independently runs a .NET 10 short job with MemoryDiagnoser.
Its mean is elapsed time, not CPU time. Allocated memory is cumulative allocation,
not peak live memory or process working set. Machine load, JIT/GC behavior, power
management, and short sample counts can affect timings. No power plan is changed.

## Measured results (2026-11-01)

Source baseline: `d404b26` plus this benchmark harness. Release build, .NET 10.0.14,
Windows 11 (10.0.26300.9711), Intel Core Ultra 9 275HX (24 cores), x64,
SDK 11.0.100-rc.2.26504.105. Values below are per complete 50,000-record input;
MB means 1,000,000 bytes. The raw matrix is saved in
`WebLogParserComparison.measured.csv`.

| Parser | Wall mean (ms) | Wall SD (ms) | Process CPU (ms) | Allocated (MB) | Allocated bytes/row |
|---|---:|---:|---:|---:|---:|
| CSV characters | 26.065 | 0.238 | 26.195 | 29.170 | 583.40 |
| CSV UTF-8 | 17.695 | 0.116 | 17.617 | 29.166 | 583.31 |
| Fixed-width | 8.618 | 0.133 | 8.581 | 8.367 | 167.35 |
| NDJSON | 48.421 | 6.180 | 46.157 | 77.413 | 1548.26 |
| JSON array | 43.341 | 5.030 | 42.383 | 77.411 | 1548.22 |
| logfmt | 16.186 | 0.185 | 16.201 | 4.589 | 91.79 |
| LTSV | 9.446 | 0.166 | 9.418 | 4.569 | 91.37 |
| Common Log | 12.177 | 0.149 | 12.118 | 4.502 | 90.05 |
| Combined Log | 15.026 | 0.198 | 14.892 | 4.561 | 91.22 |
| W3C | 11.080 | 0.166 | 11.051 | 7.270 | 145.40 |

In this run, fixed-width has the lowest CPU time. Common Log has the lowest
allocation. CSV UTF-8 uses about 33% less CPU than CSV character mode while
allocating almost the same amount. JSON/NDJSON allocate about 17 times as much
as Common Log. JSON/NDJSON have noticeably greater timing variance, so their
relative ordering should not be treated as a firm conclusion. Format sizes,
extra Combined Log fields, and W3C request reconstruction are part of this
workload and must be considered when interpreting the ranking.

The independent BenchmarkDotNet run completed all ten cases. Its elapsed means
were 26.275 ms (CSV characters), 17.155 ms (CSV UTF-8), 7.795 ms (fixed-width),
41.540 ms (NDJSON), 40.670 ms (JSON), 15.759 ms (logfmt), 9.646 ms (LTSV),
10.751 ms (Common Log), 10.819 ms (Combined Log), and 9.630 ms (W3C).
Allocation measurements agree with the matrix. The generated
`WebLogParserComparison.bdn.md` and `.bdn.csv` retain timings, uncertainty, GC
counts, and environment details. BenchmarkDotNet labels binary mebibytes as
"MB"; the table above uses decimal MB. Independent runs have different JIT and
process histories, so CPU measurements from the matrix should not be divided
by elapsed timings from BenchmarkDotNet to infer utilization.

## Validation performed

- `--validate-web-logs`: all 50,000 rows checked for each of the ten configurations.
- `--web-log-matrix`: all ten CPU/allocation measurements completed.
- BenchmarkDotNet `--filter '*WebLogParserComparison*'`: all ten cases completed.
- `dotnet build PocketCsvReader.full.sln --configuration Release`: succeeded for
  the declared .NET 8, 9, and 10 targets (six warnings in existing test/profiler code).
- .NET 10 Release tests: WebLogs 146, FixedWidth 66, NDJSON 302, JSON 887,
  KeyValue 77, and the core `CsvReaderTest` class 16; all 1,494 passed.
- An initial CSV test invocation used an unsupported `--filter-class` option;
  no tests ran in that invocation. It was rerun successfully with
  `--filter 'FullyQualifiedName~PocketCsvReader.Testing.CsvReaderTest'`.
- Not run: the full repository test suite, remaining core test classes,
  tests on .NET 8/9, other operating systems, or benchmarks on other runtimes.
