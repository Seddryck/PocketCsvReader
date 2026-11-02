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
array without invoking that grammar again over the captured object. Invalid or
incomplete records finish lexical framing before the compatible parser reports
the error, preserving recovery and trimmed EOF diagnostics. Only the failing
chunk is replayed for framing. Whitespace inside strings is fed directly to the
shared grammar, without retaining or copying the unused value. Selected composites still use the existing bounded
materialization path; unused escaped values are validated without decoding.

The fused path applies to projection with LF/CRLF, default JSON whitespace, no
comments, no record path and no stable-shape mode. Every other combination keeps
the existing NDJSON framer/parser. NDJSON dialect policy remains in the NDJSON
package; the JSON grammar exposes only its current string and pending-escape state.

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

<!-- results:start -->
| Shape | Access | CPU before → after (ms) | CPU change | Elapsed before → after (ms) | Allocated before → after (bytes) |
| --- | --- | ---: | ---: | ---: | ---: |
| cross | three values | 56.913 → 35.566 | -37.51% | 55.605 → 34.835 | 10368072 → 10368270 |
| cross | raw | 54.076 → 31.762 | -41.26% | 53.007 → 31.998 | 6008072 → 6008256 |
| large | three values | 350.694 → 84.943 | -75.78% | 349.922 → 84.694 | 5116752 → 5117040 |
| large | raw | 350.694 → 80.151 | -77.15% | 350.894 → 79.777 | 756752 → 757040 |
| small | three values | 29.313 → 19.739 | -32.66% | 29.099 → 19.807 | 4384152 → 4384336 |
| small | raw | 27.250 → 17.590 | -35.45% | 27.265 → 17.625 | 24152 → 24336 |
<!-- results:end -->

Projected NDJSON uses 33–77% less process CPU in these workloads. Full NDJSON CPU changes range from -12.9% to +2.7%; elapsed changes range from -12.4% to +2.8%, with only eight extra allocated bytes per reader. No material full-path regression is observed. JSON controls range from -5.3% to +2.7% CPU, illustrating local timing variability. These controls and the lack of affinity/power isolation limit claims about small differences.

Projection trades a small fixed amount of parser storage for lower CPU: measured allocation increases are approximately 184–288 bytes per 50,000-record operation, rather than proportional to rows. Cross-buffer bookkeeping and required selected strings still dominate allocations. The separate sync/async allocation tests cover both full and projected NDJSON and reject the former 80-byte async wrapper per row. The labeled reader dispatches to separate public and borrowed async read methods: public sources are awaited directly, preserving their wrapper and avoiding an additional async adapter or oversized combined awaiter state. No lower CPU result relies on omitting JSON validation or changing returned values.

## Review regression workloads

`regressions.jsonl` and `regressions-summary.csv` compare the original main baseline
(`4a80606`), the reviewed PR (`f449e76`) and this revision, with identical harness
source and inputs. Each workload has three samples per revision; worker order is
baseline/reviewed/fixed, then reversed, then forward again. Each worker verifies
every selected value, warms for two seconds and samples repeated operations for
two seconds. Generation, validation and hashing remain outside timing. Allocation
uses process-wide `GC.GetTotalAllocatedBytes` because the async workload hops
threads; CPU and elapsed time are separate. All workers use .NET 10.0.14.

The `x` and `spaces` cases each read one projected NDJSON object with an 8 MiB
unused string and a 4 KiB parser buffer. The `async-json` case reads 5,000 projected
JSON objects over an async-only stream that genuinely suspends before every
64-byte refill. Baseline/reviewed/fixed input hashes match for every workload.

<!-- regressions:start -->
| Workload | CPU baseline / reviewed / fixed (ms) | Elapsed baseline / reviewed / fixed (ms) | Allocated baseline / reviewed / fixed (bytes) |
| --- | ---: | ---: | ---: |
| x | 9.269 / 3.950 / 4.084 | 9.337 / 3.965 / 4.088 | 10591360 / 10591600 / 10591544 |
| spaces | 9.317 / 21.883 / 4.185 | 9.347 / 20.554 / 4.201 | 10591360 / 77547606 / 10591544 |
| async-json | 13.369 / 14.130 / 14.596 | 4.045 / 4.298 / 3.997 | 3117167 / 3576920 / 3114707 |
<!-- regressions:end -->

The whitespace fix removes both the extra scratch allocation and the CPU
regression: allocated bytes return to baseline and the fused path retains its
benefit on long strings. The async fix restores allocation to baseline instead of
adding a forwarding state machine. Thread-pool scheduling makes the short async
CPU samples variable; no CPU speedup is claimed for that case. Parser setup, pooled-buffer misses and
cross-buffer capture bookkeeping still allocate; these measurements do not imply
allocation-free parsing of arbitrary large records.

Reproduce by compiling this benchmark project against each source revision with
`ParserSourceRoot`, or use `AssemblyRoot` to bind a saved set of the three parser
DLLs. Run `Run-Regressions.ps1` with `-Baseline`, `-Reviewed` and `-Fixed` pointing to the three benchmark entry DLLs, then run `Summarize-Regressions.ps1`. `Test-ReportCulture.ps1`
checks both report generators under three cultures.

## Validation

- `dotnet build PocketCsvReader.sln -c Release -p:NuGetAudit=false` — succeeds across .NET 8/9/10; existing nullable and profiler test-platform dependency warnings remain.
- `dotnet test --project <absolute-project-csproj> -c Release -p:NuGetAudit=false` — NDJSON: 479, JSON: 1,155, KeyValue: 77 tests per framework, all passing on .NET 8, 9 and 10 (5,133 total). The final NDJSON rerun uses `--no-restore`.
- `dotnet run --project .github/tools/ParserAllocationGuard -c Release -- --self-test` — all 30 checks pass.
- `dotnet run --project .github/tools/ParserAllocationGuard -c Release -- .` — passes with 15 reviewed occurrences and no new exceptions.
- `dotnet test --project <core-test-csproj> -c Release -- --filter FullyQualifiedName~LabeledAsyncTransportTests` — the suspending public-source allocation test passes on all three frameworks (three additional executions, 5,136 total). It compares the labeled reader with a direct-await reference and rejects the reviewed binary. The new whitespace allocation tests also reject the reviewed binary in sync and async mode.
- `Test-ReportCulture.ps1` — generates and checks byte-identical CSV/Markdown under en-US, fr-BE and invariant culture; verifies invariant numbers, a raw-sample mean and plausible percentages.
- Differential probe — 5,400 baseline/fixed cases match; recovery probes and new tests verify successful reads after malformed rows, including escaped strings split across buffers.
- `Run-Regressions.ps1` and `Summarize-Regressions.ps1` — all 27 baseline/reviewed/fixed samples complete, including real async suspension and 8 MiB whitespace versus non-whitespace controls.
- `Run-Controlled.ps1` and `Summarize.ps1` — all 144 samples complete; all before/after input hashes match within each workload.

Not run: the full repository test suite, specifically the rest of PocketCsvReader.Testing, PocketCsvReader.FixedWidth.Testing, PocketCsvReader.WebLogs.Testing, PocketCsvReader.Arrow.Testing, PocketCsvReader.Chrononuensis.Testing and PocketCsvReader.SecurityLogs.Testing. All three consumers of the changed labeled-reader handoff (JSON, NDJSON and KeyValue) were tested. BenchmarkDotNet, disk-I/O, other async performance workloads, other runtime benchmark matrices and the optional PocketCsvReader.full.sln build were not run.
