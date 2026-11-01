# Optimized 50,000-record parser comparison

This rerun compares the original ten configurations and adds JSON and NDJSON with
explicit three-property projection. All twelve cases return the same
`(string Request, int Status, long Bytes)` tuple for every record. Results are
consumed immediately in a checksum, rather than stored in a result list.

## Source and environment

The parsers under test are revision `76e0b20f7c01f5be5d9a3d73aba00c0bbbe41e09`
from PR #389, which adds reusable JSON metadata and escape-decoding storage.
PR #389 was still open at measurement time; these are results for that revision,
not the unoptimized `origin/main` revision `d404b26`. The benchmark is an independent
project so it can compare source revisions through the `ParserSourceRoot` MSBuild
property without merging their production changes into this benchmark branch.

Measurements were taken on 2026-11-01, using Release builds, .NET 10.0.14, Windows
11, x64, and the Intel Core Ultra 9 275HX. The SDK is 11.0.100-rc.2.26504.105.

## Workload and methodology

- Exactly 50,000 deterministic synthetic requests per operation; every projected
  value and the exact row count are checked during setup. Checksums are checked
  during each matrix invocation.
- The input encodings and sizes match the original comparison. JSON and NDJSON
  include the timestamp's `\u002B` escape produced by `System.Text.Json`; the
  projected cases use exactly the same input bytes as their unprojected counterparts.
- Generic formats and Common Log contain host, identity, user, timestamp, request,
  status, and response bytes. Combined Log also contains referrer and user agent.
  W3C stores method/URI and date/time separately; rebuilding the common request
  string is included in its measurements.
- Inputs are UTF-8 without BOM with LF newlines, read through fresh read-only
  memory streams. Generation, encoding, configuration, and setup validation are
  excluded. Stream/reader creation, parsing, typed access, projection, consumption,
  and disposal are included.
- All parsers use 4 KiB buffers, no read-ahead, no initial row count, and no position
  tracking. CSV encoding probing is disabled. JSON/NDJSON projection uses
  `WithProjection(p => p.Property("request").Property("status").Property("bytes"))`;
  no stable-shape or ordered-projection assumption is enabled.
- Each matrix case warms up for at least two seconds, then measures seven batches
  lasting at least one second each. Full GCs before batches are excluded.
  `Stopwatch` measures wall time, `Process.TotalProcessorTime` measures process CPU
  time including GC threads, and `GC.GetAllocatedBytesForCurrentThread` measures
  managed allocations on the synchronous calling thread.
- CPU accounting has Windows timer quantization error. Timings depend on host
  load, power management, JIT, GC, and process history. The runs are sequential;
  no other builds or tests are started during measurements. No power plan is changed.
- Memory is cumulative allocation, not peak live memory or working set. MB in this
  report is decimal (1,000,000 bytes).

The original run's raw matrix is preserved in `original.csv`. Comparing its timings
with this later run is a historical comparison, not a controlled same-process A/B
test. The current twelve cases are measured together using the same harness.

## Reproduce

Set `ParserSourceRoot` to a checkout of the exact parser revision to measure.
Keep it set for BenchmarkDotNet so its generated build also uses that source.
The environment variable below applies to the current PowerShell session only.
Without it, the project uses the repository containing this benchmark.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:ParserSourceRoot = 'D:\users\seddr\Projects\PocketCsvReader\.worktrees\json-allocations'
git -C $env:ParserSourceRoot rev-parse HEAD
dotnet build benchmarks/WebLogParserComparison/WebLogParserComparison.csproj -c Release
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --validate
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --matrix
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --filter '*WebLogParserComparison*'
```

The benchmark validates 600,000 projected rows across its twelve setup cases.
Input size and allocation per row are included in the raw matrix output.

## Current matrix results

Values are per full 50,000-record operation, sorted by process CPU time. Wall SD
is the sample standard deviation of the seven batch means. Raw values are saved
in `optimized.csv`.

| Parser | CPU (ms) | Wall mean (ms) | Wall SD (ms) | Allocated (MB) | Bytes/row |
|---|---:|---:|---:|---:|---:|
| Fixed-width | 10.326 | 10.406 | 0.556 | 8.367 | 167.35 |
| LTSV | 14.412 | 14.511 | 1.326 | 4.569 | 91.37 |
| W3C | 14.719 | 14.850 | 1.471 | 7.270 | 145.40 |
| Common Log | 16.905 | 17.082 | 0.742 | 4.502 | 90.05 |
| Combined Log | 19.372 | 19.473 | 1.163 | 4.561 | 91.22 |
| CSV UTF-8 | 21.215 | 21.357 | 1.038 | 29.166 | 583.31 |
| logfmt | 23.437 | 23.634 | 2.963 | 4.589 | 91.79 |
| JSON projected | 24.519 | 24.779 | 0.831 | 4.612 | 92.24 |
| CSV characters | 31.328 | 31.196 | 0.765 | 29.170 | 583.40 |
| NDJSON projected | 37.569 | 37.487 | 2.831 | 4.614 | 92.28 |
| JSON | 43.336 | 43.543 | 5.505 | 8.613 | 172.25 |
| NDJSON | 45.101 | 45.389 | 4.136 | 8.615 | 172.29 |

Fixed-width uses the least CPU in this run. Common Log allocates the least.
Projected JSON/NDJSON allocate about 92 bytes/row, very close to the key/value
and web-log parsers. Ordinary JSON/NDJSON allocate slightly more than fixed-width
and far less than CSV, although their CPU cost is still higher on this workload.

Within this run, projection reduces JSON CPU by about **43%** and NDJSON CPU by
about **17%**, while reducing their allocation by about **46%** relative to
ordinary optimized parsing. The non-projected paths still decode the unused
escaped timestamp; projection avoids that materialization.

Compared with the original ordinary-parser allocation, JSON drops from 77.411 MB
to 8.613 MB and NDJSON from 77.413 MB to 8.615 MB, both about **89% less**.
Adding explicit projection brings both to approximately 4.6 MB, about **94% less**
than their original non-projected allocation. Other parsers' allocation totals
are essentially unchanged. The current timings are generally slower than the
original session and the JSON cases have greater variance; the historical
CPU differences should not be attributed solely to the optimization. Use the
current run to compare parsers, with short-run timing uncertainty in mind.

## Independent BenchmarkDotNet check

All twelve .NET 10 ShortRun cases completed with MemoryDiagnoser. Allocation
agrees with the matrix, including 8.21/8.22 MiB for ordinary JSON/NDJSON and
approximately 4.4 MiB for both projected cases. The generated `optimized.bdn.md`
and `optimized.bdn.csv` retain elapsed times, uncertainty, GC counts, and environment
details. BenchmarkDotNet labels binary MiB as "MB"; the matrix table above uses
decimal MB. Its elapsed time is not process CPU time, and should not be combined
with CPU figures from the separate matrix to infer utilization. ShortRun's three
measurement iterations give broad confidence intervals, especially for closely
ranked parsers.

The JSON assembly used by the matrix matched the optimized source checkout's
assembly by SHA-256 (`85B91655EA58621B3AEEFEA6E5D2FC404FFB7B69890401BEDF180E561E7AA31C`).
The independently built BenchmarkDotNet cases' JSON allocation also confirms that
they used the optimized path rather than the old 77 MB implementation.

## Validation performed

- `--validate`: all three returned values checked on each of 50,000 rows for
  each of twelve cases (600,000 row checks).
- `--matrix`: all twelve CPU/allocation cases completed; checksums validated
  on every invocation.
- BenchmarkDotNet: all twelve cases completed against the selected optimized source.
- Comparison project Release build: succeeded on .NET 8, 9, and 10 with no warnings
  or errors, referencing the selected optimized parser projects.
- Full repository solution Release build: succeeded across its declared targets,
  with seven warnings in existing core test/profiler code and dependencies.
- `git diff --check`: passed.
- Not run: unit test suites (this change only adds a benchmark/report), benchmarks
  on .NET 8/9, other operating systems, disk-I/O benchmarks, or new workloads for
  malformed input, missing values, Unicode/escaping variety, or nested JSON.
  Production optimization regression tests were previously run separately in
  PR #389; those earlier executions are not counted as tests run for this rerun.
