# Source position tracking benchmarks

Measured on 2026-10-22, Windows x64, Intel Core Ultra 9 275HX, .NET 10.0.12. The unchanged parser baseline is `5c9ce0498d577360e71b6fd86b4cf0314f8217ae` plus this benchmark harness; baseline binaries were built and saved before implementation. The harness uses setup-only reflection to enable the new option when present, allowing exactly the same source to run against the baseline. The baseline runs only disabled cases because the option does not exist there.

## Full comparison matrix

Each operation reads 2,000 records from memory with read-ahead disabled. Data creation is outside measurements. The short runner warms each case for 250 ms, collects seven samples of at least 150 ms, and reports elapsed time per operation and current-thread allocations. The matrix uses `DOTNET_TieredCompilation=0` for both binaries to avoid tier transitions during short samples. This is a diagnostic configuration, not a claim about default-JIT production throughput. One baseline sweep ran before and one after the final feature sweep; the raw files also expose between-sweep timing drift, which limits precision of baseline ratios; the table averages their means. Raw CSV files include per-sample standard deviations and derived records/second.

```powershell
$env:DOTNET_TieredCompilation = '0' # process-local setting
# Run the saved pre-feature benchmark binary with the same argument for the baseline.
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --position-tracking-matrix
```

The table reports time changes: positive percentages mean slower. Baseline raw sweeps are [before](PositionTracking.baseline-before.csv) and [after](PositionTracking.baseline-after.csv); the [feature CSV](PositionTracking.measured.csv) includes enabled and disabled cases.
| Parser / scenario | Baseline µs | Disabled µs ± SD | Enabled µs ± SD | Disabled vs baseline | Enabled vs disabled |
| --- | ---: | ---: | ---: | ---: | ---: |
| DecodedChars / ShortSkipped | 594.50 | 588.94 ± 4.87 | 626.09 ± 14.18 | -0.9% | 6.3% |
| DecodedChars / WideUnicodeSkipped | 2742.00 | 2970.65 ± 52.59 | 3058.89 ± 46.63 | 8.3% | 3.0% |
| DecodedChars / MultilineStrings | 1086.59 | 1049.51 ± 18.51 | 1158.50 ± 24.19 | -3.4% | 10.4% |
| DecodedChars / Numeric | 952.19 | 983.71 ± 33.38 | 1033.15 ± 31.78 | 3.3% | 5.0% |
| DecodedChars / WideStrings | 4285.45 | 3521.71 ± 21.28 | 3578.69 ± 25.43 | -17.8% | 1.6% |
| DecodedChars / CustomNewlines | 736.61 | 770.75 ± 5.18 | 882.98 ± 4.34 | 4.6% | 14.6% |
| DecodedChars / CrossBuffer | 9407.22 | 8191.06 ± 60.62 | 9311.68 ± 171.75 | -12.9% | 13.7% |
| Utf8Bytes / ShortSkipped | 402.69 | 397.18 ± 3.14 | 427.41 ± 5.34 | -1.4% | 7.6% |
| Utf8Bytes / WideUnicodeSkipped | 2294.70 | 2306.02 ± 10.03 | 2333.67 ± 24.77 | 0.5% | 1.2% |
| Utf8Bytes / MultilineStrings | 849.76 | 842.42 ± 6.63 | 919.61 ± 8.37 | -0.9% | 9.2% |
| Utf8Bytes / Numeric | 506.25 | 493.66 ± 4.50 | 524.86 ± 4.65 | -2.5% | 6.3% |
| Utf8Bytes / WideStrings | 3403.62 | 3440.38 ± 141.69 | 3410.41 ± 24.21 | 1.1% | -0.9% |
| Utf8Bytes / CustomNewlines | 539.16 | 526.66 ± 5.57 | 648.79 ± 6.30 | -2.3% | 23.2% |
| Utf8Bytes / CrossBuffer | 1250.12 | 1245.66 ± 17.34 | 1410.99 ± 25.08 | -0.4% | 13.3% |

`ShortSkipped` advances without requesting values. `WideUnicodeSkipped` has 24 Unicode fields and also requests no values. `Numeric` requests typed integers; `WideStrings` requests all 24 strings; `MultilineStrings` requests quoted strings containing CRLF. `CustomNewlines` uses `||` record terminators and embedded CRLF, LF, and CR. `CrossBuffer` uses 300-character fields with a 64-character input buffer. Other cases use 4,096-character/byte buffers.

The final code preserves the native UTF-8 `SearchValues<byte>` / `IndexOfAny` scanner and bulk copies. Enabled tracking searches consumed spans for CR/LF, including bulk payload with custom terminators. Disabled tracking bypasses those searches. Tracking counts native span lengths without decoding payload, and metadata getters do not project records to characters. These measurements include maintaining positions but do not separately measure callers formatting or logging the metadata.

## Allocations

Bytes below are per complete 2,000-record operation, including existing parser/record/field allocations. Tracking adds approximately 104 bytes per reader, rather than an allocation for each row. Disabled-reader layout grows by 16 bytes on the character path and 8 bytes on the UTF-8 path. Small byte-level differences on larger workloads reflect integer rounding and runtime bookkeeping. Focused tests compare the enabled-minus-disabled allocation delta at 20 and 2,000 rows to guard against per-record metadata allocation.
| Parser / scenario | Baseline bytes/op | Disabled bytes/op | Enabled bytes/op |
| --- | ---: | ---: | ---: |
| DecodedChars / ShortSkipped | 554336 | 554352 | 554456 |
| DecodedChars / WideUnicodeSkipped | 2910016 | 2910032 | 2910136 |
| DecodedChars / MultilineStrings | 843896 | 843912 | 844016 |
| DecodedChars / Numeric | 555896 | 555912 | 556016 |
| DecodedChars / WideStrings | 4458609 | 4458624 | 4458728 |
| DecodedChars / CustomNewlines | 554336 | 554352 | 554456 |
| DecodedChars / CrossBuffer | 554338 | 554354 | 554458 |
| Utf8Bytes / ShortSkipped | 549783 | 549791 | 549895 |
| Utf8Bytes / WideUnicodeSkipped | 2906366 | 2906376 | 2906479 |
| Utf8Bytes / MultilineStrings | 837787 | 837795 | 837900 |
| Utf8Bytes / Numeric | 549783 | 549791 | 549895 |
| Utf8Bytes / WideStrings | 4442388 | 4442397 | 4442501 |
| Utf8Bytes / CustomNewlines | 549727 | 549735 | 549839 |
| Utf8Bytes / CrossBuffer | 549784 | 549792 | 549897 |

## Investigation of the disabled path

The diagnostic matrix found a 8.3% character-path slowdown for wide Unicode rows with tiered compilation disabled. This was investigated by moving position bookkeeping outside the normal character token-completion path and retaining the original full-buffer read call while filling only short reads. The disabled scanner now bypasses tracking scans; the remaining diagnostic difference should not be described as zero overhead.

A separate BenchmarkDotNet check with normal tiered compilation used the same saved baseline and feature binaries, in-process toolchain, five warmups, ten measured iterations, 500 ms requested iteration time, and one launch. These measurements isolate the wide-Unicode skipped-column scenario:

| Parser | Baseline mean ± SD | Disabled mean ± SD | Enabled mean ± SD |
| --- | ---: | ---: | ---: |
| Characters | 2.124 ± 0.216 ms | 2.155 ± 0.196 ms | 2.455 ± 0.024 ms |
| UTF-8 bytes | 1.877 ± 0.239 ms | 1.921 ± 0.014 ms | 1.970 ± 0.012 ms |

Full reports: [baseline](PositionTracking.bdn-baseline.md), [feature modes](PositionTracking.bdn-feature.md). The disabled means were about 1.5% slower for characters and 2.3% slower for UTF-8, with overlapping baseline confidence intervals. Enabled versus disabled time increased about 13.9% for characters and 2.6% for UTF-8 in this scenario. Earlier sweeps showed different means, including occasional apparent enabled-mode speedups; those are not evidence that tracking intrinsically speeds parsing up. The broad baseline confidence intervals and between-sweep timing drift demonstrate sensitivity to JIT specialization and machine conditions. Do not extrapolate these results into a universal speedup, zero disabled overhead, or an exact overhead percentage for production workloads.

```powershell
# In a fresh shell with normal tiered compilation:
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*PositionTracking*WideUnicodeSkipped*' --inProcess --warmupCount 5 --iterationCount 10 --iterationTime 500 --launchCount 1
# For a longer isolated full-matrix BenchmarkDotNet run:
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*PositionTracking*' --warmupCount 5 --iterationCount 10 --launchCount 2
```

The full in-memory short matrix and the targeted in-process BenchmarkDotNet check were run. The longer isolated full matrix, .NET 8/9 performance comparisons, disk-I/O benchmarks, and compression-specific performance runs were not run. Correctness tests cover all three supported frameworks, compressed/non-seekable inputs, sync/async parity, allocation scaling, and metadata access without UTF-8 character projection.
## Validation performed

Release solution build: `dotnet build PocketCsvReader.full.sln -c Release`, plus the key-value test project (not included in that solution). All supported target frameworks built: net8.0, net9.0, net10.0. Existing nullable/Profiler dependency and generated-entry-point warnings remain; there were no build errors.

The following passed on each of net8.0, net9.0, and net10.0:

| Test selection | Tests per framework |
| --- | ---: |
| Core position, UTF-8 input, record parser, bad-data, and single/double/stream-buffer groups | 231 |
| Core AsyncDataReaderTest, DbDataReaderAdapterTest, CsvDataReaderTest, CsvBatchDataReaderTest | 120 |
| Fixed-width suite | 20 |
| Web-log suite | 17 |
| NDJSON suite | 271 |
| JSON suite | 59 |
| Key-value suite | 30 |

Tests were executed directly through each built NUnit test application using `--filter`, `--progress off`, and `--output Minimal`. The initial `dotnet test --filter` invocation ran zero tests under this repository's runner configuration and was replaced by these successful direct invocations.

The full core test suite and unrelated Arrow/Chrononuensis test suites were not run. The benchmark runs and exclusions are listed above. `git diff --check` passed.
