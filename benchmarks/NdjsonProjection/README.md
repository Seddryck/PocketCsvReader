# Fused NDJSON projection comparison

Issue #395. Baseline: `4a80606` (latest `origin/main` when the task started).
The same harness source is built once against that baseline and once against the
candidate. Run on Windows x64, .NET 10, Release. This is a controlled local
comparison, not a general JSON-versus-NDJSON ranking.

## Reproduce

Create a baseline checkout at `4a80606`, then from the candidate worktree:

```powershell
dotnet build benchmarks/NdjsonProjection/NdjsonProjection.csproj -c Release -f net10.0 -p:ParserSourceRoot=<baseline-root> -o artifacts/ndjson-before
dotnet build benchmarks/NdjsonProjection/NdjsonProjection.csproj -c Release -f net10.0 -o artifacts/ndjson-after
./benchmarks/NdjsonProjection/Run-Controlled.ps1 -Before <absolute-before-dll> -After <absolute-after-dll>
```

`results.csv` retains every sample and input SHA-256. Each worker generates input
and validates every `(Request, Status, Bytes)` before timing, then warms for at
least two seconds and samples repeated whole-input operations for at least two
seconds. Three baseline/candidate pairs alternate AB, BA, AB for every workload.
Elapsed time, process CPU and current-thread managed allocation are measured
separately, normalized per 50,000 records. Input generation, expected values,
validation, warmup, explicit collection, hashing and worker startup are outside
the sample. Synchronous reads keep allocation accounting on one thread. These
measurements do not cover async scheduling, disk latency, or every dialect.
Workers run sequentially without concurrent builds/tests; CPU affinity, power
policy and machine-wide configuration are unchanged.

The matrix covers full and projected JSON/NDJSON, raw reads and three-value access,
and three record shapes:

| Shape | Extra unused string | Character buffer | Purpose |
| --- | ---: | ---: | --- |
| small | 0 | 65,536 | ordinary records, mostly within one buffer |
| cross | 300 | 128 | every record crosses several buffers |
| large | 8,000 | 65,536 | long unselected values with occasional refills |

JSON uses a top-level array and NDJSON uses LF-delimited records; the property
values are identical. Selected text allocation is required output and is reported
separately from raw reads. Aggregate checksums are checked during timing; all
individual selected values are checked outside timing.

## Parser and ownership decisions

`ProjectedNdjsonFramer` locates LF/CRLF boundaries using chunk searches and feeds
the shared `ProjectedJsonValueFramer` while capturing the record. It validates the
whole object and trailing content and publishes the parser-owned projected field
array without invoking that grammar again over the captured object. Deferred
whitespace uses reusable storage so trimming and truncated-input diagnostics stay
compatible across refills. Selected composites still use the existing bounded
materialization path; unused escaped values are validated without decoding.

The fused path applies to projection with LF/CRLF, default JSON whitespace, no
comments, no record path and no stable-shape mode. Every other combination keeps
the existing NDJSON framer/parser. NDJSON dialect policy remains in the NDJSON
package; the JSON grammar exposes only its current string-token state.

`BufferedRecordReader` owns the capture: a same-buffer record is a borrowed memory
slice; cross-buffer records use its existing pooled segments and coalescing.
`Protect` precedes lookahead so a refill preserves the current record. Character
memory and field metadata remain borrowed until advancement/disposal. Retained
strings and arrays keep their existing independent lifetime. Sync and async feed
the same framing state. The NDJSON source opts into the shared current-record
transport, and the labeled reader uses that transport asynchronously; public
source reads still materialize their required `RecordMemory`. The two stale
NDJSON wrapper exceptions were removed from the allocation guard, with none added.

## Measured results

Means of three samples, per 50,000 records. Negative CPU change means less CPU.
See `summary.csv` for all 24 cases and `results.csv` for all 144 samples.

| Shape | Access | CPU before → after (ms) | CPU change | Elapsed before → after (ms) | Allocated before → after (bytes) |
| --- | --- | ---: | ---: | ---: | ---: |
| cross | three values | 67864.00 → 44317.00 | -347.0% | 74189.00 → 52092.00 | 10368072 → 10368344 |
| cross | raw | 64118.00 → 37684.00 | -4123.0% | 62394.00 → 38279.00 | 6008072 → 6008312 |
| large | three values | 518229.00 → 93987.00 | -8186.0% | 520024.00 → 93915.00 | 5116752 → 5117096 |
| large | raw | 459375.00 → 91732.00 | -8003.0% | 502796.00 → 118638.00 | 756752 → 757096 |
| small | three values | 35993.00 → 24629.00 | -3157.0% | 36239.00 → 24753.00 | 4384152 → 4384392 |
| small | raw | 32375.00 → 21313.00 | -3417.0% | 32613.00 → 21413.00 | 24152 → 24392 |

Projected NDJSON uses 32–82% less process CPU in these workloads. Full NDJSON CPU changes range from -4.6% to +1.5%; elapsed changes range from -5.3% to +1.6%, with only eight extra allocated bytes per reader. No material full-path regression is observed. JSON controls range from -2.2% to +5.3% CPU, illustrating local timing variability. These controls and the lack of affinity/power isolation limit claims about small differences.

Projection trades a small fixed amount of parser storage for lower CPU: measured allocation increases are approximately 240–344 bytes per 50,000-record operation, rather than proportional to rows. Cross-buffer bookkeeping and required selected strings still dominate allocations. The separate sync/async allocation tests cover both full and projected NDJSON and reject the former 80-byte async wrapper per row. No lower CPU result relies on omitting JSON validation or changing returned values.

## Validation

- `dotnet build PocketCsvReader.sln -c Release -p:NuGetAudit=false` — succeeds across .NET 8/9/10; existing nullable and profiler test-platform dependency warnings remain.
- `dotnet test --project <absolute-project-csproj> -c Release -p:NuGetAudit=false` — NDJSON: 361, JSON: 1,155, KeyValue: 77 tests per framework, all passing on .NET 8, 9 and 10 (4,779 total). The final NDJSON rerun uses `--no-restore`.
- `dotnet run --project .github/tools/ParserAllocationGuard -c Release -- --self-test` — all 30 checks pass.
- `dotnet run --project .github/tools/ParserAllocationGuard -c Release -- .` — passes with 15 reviewed occurrences and no new exceptions.
- `Run-Controlled.ps1` and `Summarize.ps1` — all 144 samples complete; all before/after input hashes match within each workload.

Not run: the full repository test suite, specifically PocketCsvReader.Testing, PocketCsvReader.FixedWidth.Testing, PocketCsvReader.WebLogs.Testing, PocketCsvReader.Arrow.Testing, PocketCsvReader.Chrononuensis.Testing and PocketCsvReader.SecurityLogs.Testing. All three consumers of the changed labeled-reader handoff (JSON, NDJSON and KeyValue) were tested. BenchmarkDotNet, disk-I/O, async performance, other runtime benchmark matrices and the optional PocketCsvReader.full.sln build were not run.
