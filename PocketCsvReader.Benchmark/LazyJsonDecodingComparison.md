# Lazy JSON and NDJSON string decoding

Implements issue #390 on top of `origin/main` at `5791ad2`.

## Behavior

Parsing validates string escapes with the existing allocation-free `SkipString`
scanner, including hexadecimal digits, escape characters, and Unicode surrogate
pairs. It records source spans and the escaped flag without building decoded
value strings. Escaped property names still decode during parsing for name lookup
and record-path selection.

The first logical access to an escaped value decodes it with the existing string
decoder and stores the independently owned string in `FieldSpan.DecodedValue`.
Scalar access, array access, array validation, custom parsers, and materialization
share the same field-memory hook. Array children are passed by reference so their
decoded strings also remain cached. Replacing metadata for the next row clears
decoded values, including when NDJSON stable-shape buffers are recycled.

Raw getters preserve the source text without decoding. Null checks on the concrete
JSON/NDJSON readers inspect metadata without decoding. Default `GetString` returns
the cached decoded string directly; custom string pools still run as before.
Derived readers retain their value transformations. Typed/custom parsers and
materializers can still allocate their own output objects, independently of the
cached decoded input text.

No projection, stable-shape assumption, or new configuration is required. Both
`Read` and `ReadAsync` reject invalid escapes even in unaccessed fields, nested
values, and unprojected properties. Existing projection, ordered projection,
JSONC, and record-path behavior remain covered by tests.

## Measurements

`LazyJsonDecodingComparison` uses the existing deterministic 50,000-row,
seven-property web-log workload. Each operation consumes request, status, and
response bytes. The accessed-timestamp control also reads the escaped timestamp
with `GetString` once per row. Neither case configures projection. Setup validates
every projected row and the additional decoded timestamp checksum outside timing.

Baseline and lazy versions use the same harness. The baseline was compiled in an
isolated local probe against the unchanged source tree equivalent to `5791ad2`.
The two matrix runs were sequential, with no concurrent build or test workload.
Both use 4 KiB buffers, UTF-8 memory streams with LF newlines, no read-ahead, no
initial row counting, and no position tracking. Input generation and encoding
are excluded; stream/reader creation, parsing, access, checksum consumption, and
disposal are included.

Each case warms up for at least two seconds, then takes seven samples of at least
one second each. Wall time uses `Stopwatch`, process CPU time uses
`Process.TotalProcessorTime`, and allocations use
`GC.GetAllocatedBytesForCurrentThread`. Full GC before samples is excluded.
Machine: Intel Core Ultra 9 275HX, Windows 11, x64, .NET 10.0.14,
SDK 11.0.100-rc.2.26504.105; measured 2026-11-01.

All figures are per 50,000 records. MB means 1,000,000 allocated bytes, not peak
live memory or working set.

| Format | Timestamp access | Before (MB) | Lazy (MB) | Reduction | CPU before (ms) | CPU lazy (ms) |
|---|---|---:|---:|---:|---:|---:|
| JSON | Untouched | 8.613 | 4.612 | 46.4% | 35.911 | 33.204 |
| JSON | Accessed | 12.613 | 8.613 | 31.7% | 38.477 | 37.333 |
| NDJSON | Untouched | 8.615 | 4.614 | 46.4% | 36.181 | 38.333 |
| NDJSON | Accessed | 12.615 | 8.615 | 31.7% | 37.483 | 37.312 |

An untouched timestamp saves about 80 bytes/row by avoiding its decoded string.
An accessed timestamp also saves about 80 bytes/row because the old `GetString`
copied the eagerly decoded text into a second string, whereas default `GetString`
now returns the cached string. The tiny per-input workspace differences are
included. Allocation reductions are consistent; CPU measurements vary, including
a slower NDJSON untouched matrix case. This run does not establish a consistent
CPU improvement.

An independent BenchmarkDotNet short run completed all four lazy cases and
confirmed allocation totals. Its elapsed means were 34.90/41.46 ms for JSON
untouched/accessed and 32.98/35.14 ms for NDJSON untouched/accessed. Timing variance
is appreciable; the generated reports include standard deviations and uncertainty.
BenchmarkDotNet labels binary mebibytes as "MB"; this report uses decimal MB.

Raw reports: `LazyJsonDecodingComparison.before.csv`, `.after.csv`, `.bdn.md`,
and `.bdn.csv`.

```powershell
dotnet build PocketCsvReader.Benchmark/PocketCsvReader.Benchmark.csproj -c Release -f net10.0
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --lazy-json-matrix
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --filter '*LazyJsonDecodingComparison*'
```

## Validation

- JSON test project: 1,155 passed on each of .NET 8, 9, and 10. Includes both-format
  tests of deferred access, raw/null getters, repeated access allocations, cache
  resets, escaped names, custom parsers/pools, schema arrays, materialization,
  derived readers, and invalid unaccessed escapes across 16-character buffers.
- NDJSON test project: 310 passed on each of .NET 8, 9, and 10, including stable
  metadata/child-buffer recycling and invalid escapes on subsequent records.
- .NET 10 core: 161 passed, filtered to `BaseDataRecordTests`, `CsvReaderTest`,
  `SchemaArrayTest`, `CsvArrayStringTest`, `ObjectArrayRowsTest`,
  `SpanCustomParserTests`, and `IsValidTests`.
- .NET 10 KeyValue 77, FixedWidth 66, and WebLogs 146 tests passed.
- Total final focused test executions: **4,845 passed**, none skipped.
- Full Release solution build succeeded across its declared .NET 8/9/10 targets;
  six warnings remain in existing test/profiler code and dependencies.
- Four-case before/after matrices and all four independent BenchmarkDotNet cases
  completed; `git diff --check` passed.
- Not run: the full repository suite, remaining core test classes, non-JSON
  parser tests on .NET 8/9, other operating systems, or performance measurements
  on runtimes other than .NET 10.
