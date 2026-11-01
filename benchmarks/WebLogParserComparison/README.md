# Optimized 50,000-record parser comparison

This rerun compares the original ten configurations and adds JSON and NDJSON with
explicit three-property projection. All twelve cases return the same
`(string Request, int Status, long Bytes)` tuple for every record. Results are
consumed immediately in a checksum, rather than stored in a result list.

## Source and environment

The benchmark branch was rebased onto `origin/main` at
`b359baf1be3d2fcb278f7f05fb28a5acbc16f422`. This includes reusable JSON metadata
and decoding storage (#389), plus on-demand string decoding (#392). Parser source
is taken directly from this rebased checkout, with no external source override.
The harness revision used for measurement is `326ceac779d57e12a2e1da45bea416fd087e86bb`;
subsequent changes only refresh this report and its saved results.

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

The original run's raw matrix is preserved in `original.csv`; `previous.csv`
preserves the prior run against the metadata-reuse changes before lazy decoding.
Comparisons between sessions are historical, not controlled same-process A/B
tests. The current twelve cases are measured together using the same harness.

## Reproduce

Run from this rebased repository root. Remove any prior source override so both
the matrix and BenchmarkDotNet's generated build use the current checkout.
The optional `ParserSourceRoot` property remains available for explicit comparisons
against another source checkout, but was not set for this run.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Remove-Item Env:ParserSourceRoot -ErrorAction SilentlyContinue
git rev-parse HEAD
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
| Fixed-width | 8.738 | 8.753 | 0.144 | 8.367 | 167.35 |
| LTSV | 9.841 | 9.869 | 0.107 | 4.569 | 91.37 |
| W3C | 11.120 | 11.182 | 0.201 | 7.270 | 145.40 |
| Common Log | 12.464 | 12.520 | 0.210 | 4.502 | 90.05 |
| Combined Log | 14.418 | 14.413 | 0.311 | 4.561 | 91.22 |
| logfmt | 16.090 | 16.109 | 0.105 | 4.589 | 91.79 |
| CSV UTF-8 | 17.966 | 17.909 | 0.083 | 29.166 | 583.31 |
| JSON projected | 19.687 | 19.685 | 0.171 | 4.612 | 92.24 |
| CSV characters | 26.337 | 26.247 | 0.274 | 29.170 | 583.40 |
| NDJSON projected | 28.954 | 29.028 | 0.126 | 4.614 | 92.28 |
| JSON | 29.937 | 29.995 | 0.110 | 4.612 | 92.25 |
| NDJSON | 31.635 | 31.793 | 1.248 | 4.614 | 92.28 |

Fixed-width uses the least CPU in this run, while Common Log allocates the least.
With on-demand string decoding on `main`, ordinary JSON and NDJSON no longer
materialize the unused escaped timestamp. All four JSON configurations now
allocate approximately **4.6 MB**, or **92 bytes/row**. Projection's allocation
advantage has therefore disappeared on this workload. In this matrix, projection
uses approximately **34% less CPU for JSON** and **8% less for NDJSON** compared
with ordinary parsing. The independent BenchmarkDotNet run did not reproduce the
NDJSON timing benefit, so that small gain should not be treated as reliable.
Projection still validates skipped input.

The prior buffer-reuse-only run (`previous.csv`) allocated 8.613 MB for JSON and
8.615 MB for NDJSON. Lazy decoding removes approximately 4 MB more per operation,
a **46% reduction** in ordinary-parser allocation. Compared with the original
77.4 MB results (`original.csv`), ordinary JSON/NDJSON now allocate about **94% less**.
Other parsers' allocation totals are essentially unchanged.

Current CPU timings are lower than in the prior session across many unchanged
parsers too. Do not attribute the entire historical CPU change to lazy decoding;
compare projection against ordinary parsing within this current run. Closely
ranked configurations should be interpreted with short-run uncertainty in mind.

## Independent BenchmarkDotNet check

All twelve .NET 10 ShortRun cases completed with MemoryDiagnoser. Allocations agree
with the matrix: ordinary and projected JSON/NDJSON all allocate approximately
4.4 MiB (4.6 decimal MB). The generated `optimized.bdn.md` and `.bdn.csv` preserve
elapsed times, uncertainty, GC counts, and environment details.

| JSON configuration | BenchmarkDotNet elapsed mean (ms) |
|---|---:|
| JSON | 28.768 |
| JSON projected | 24.886 |
| NDJSON | 28.964 |
| NDJSON projected | 34.231 |

JSON projection is faster in both measurement methods, although the size of the
benefit differs. NDJSON projection is slower in this independent run despite a
small CPU improvement in the matrix. Separate process/JIT histories and short-run
variation mean projection is not a demonstrated general CPU win for NDJSON here.
Its allocation is now effectively identical to ordinary lazy decoding.

BenchmarkDotNet labels binary MiB as "MB"; the matrix uses decimal MB. Its elapsed
time is not process CPU time, and should not be combined with CPU figures from
the separate matrix to infer utilization. ShortRun's three measurement iterations
give broad confidence intervals, especially for closely ranked parsers.

## Validation performed

- `--validate`: all three returned values checked on 50,000 rows for each of
  twelve cases (600,000 row checks).
- `--matrix`: all twelve CPU/allocation cases completed; checksums checked on
  every invocation.
- BenchmarkDotNet: all twelve cases completed against this rebased checkout.
- .NET 10 Release unit tests: JSON **1,155 passed**, NDJSON **310 passed**;
  **1,465 total**, no failures or skips.
- Comparison project Release build: succeeded for .NET 8, 9, and 10 with no
  warnings or errors.
- Full solution Release build: succeeded across declared targets; seven warnings
  remain in existing core test/profiler code and dependencies.
- Parser allocation guard: passed, with 19 reviewed occurrences.
- `git diff --check`: passed.
- Not run: the full repository test suite, unrelated parser unit test projects,
  .NET 8/9 unit tests or benchmarks, other operating systems, disk-I/O benchmarks,
  or new malformed-input, Unicode, missing-value, or nested-document workloads.
