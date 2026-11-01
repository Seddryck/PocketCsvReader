# CSV storage reuse and full parser comparison

For the latest rebased W3C/Common/Combined investigation, see
[the web-log allocation follow-up](WEBLOG-ALLOCATIONS.md).

For the controlled CPU investigation and subsequent source-dispatch update, see
[the controlled CSV UTF-8 follow-up](CONTROLLED-CSV.md). It did not reproduce the
previously suggested regression. The twelve-parser table below is the historical
measurement at PR revision `93a566a`, before that follow-up.

## Change and ownership review

The baseline is `origin/main` at `b359baf`, including lazy JSON string decoding.
On the seven-field log workload, both CSV modes previously allocated a 416-byte
metadata array and an 80-byte `RecordMemory` wrapper per row before accessing
any values: 24.8 MB of avoidable bookkeeping per 50,000 records.

The same lifecycle problem has been addressed recently in several formats:

These commits landed on 2026-10-31 and 2026-11-01, immediately before this change.

| Format | Recent fix | Repeated issue |
|---|---|---|
| Common/W3C logs | `c19dddd`, PR #376 | Temporary row/field materialization |
| Key/value | `1a700a5`, PR #383 | Repeated field metadata allocations |
| Fixed-width | `6f0f970`, `bc9b533` | Repeated character storage and record copying |
| JSON/NDJSON | `5791ad2`, PR #389 | Repeated root metadata and decoding workspace |
| JSON/NDJSON | `b359baf`, PR #392 | Decoding escaped values the caller never requests |

Rather than add another format-local copy of the fix, this implementation adds:

- Core `FieldMetadataBuffer`: explicit `PublishBorrowed` and `CopyOwned` operations.
  CSV, JSON, and key/value use the same publication primitive. Borrowed arrays have
  the exact current field count and are reused while that count stays unchanged.
- Core `CurrentRecord` and `ICurrentRecordSource`: value-based internal transport
  for character or UTF-8 records, shared by synchronous and asynchronous CSV reads.
- `BaseDataReader.ReadSource`/`ReadSourceAsync`: select the internal transport only
  for safe opted-in sources; custom public overrides keep their existing entry points.
- `BaseRawRecord`: stores the view directly and exposes requested field memory
  without constructing a heap wrapper. Its compatibility `Record` property remains
  lazy and can snapshot metadata when its existing lifetime requires that.
- CSV's internal character and UTF-8 reader paths reuse metadata and avoid wrappers
  during ordinary field access. Public parser calls still copy their top-level
  metadata. Retained public mapper arguments and legacy CSV wrapper exposure keep
  independent metadata too. Character/byte views retain their documented next-read
  lifetime; this does not promise persistent input text or deep metadata snapshots.
- Fixed-width synchronous readers use the same base view/access helpers instead
  of materializing a wrapper. Their existing borrowed metadata contract is preserved.
  Fixed-width's public asynchronous result still requires its compatibility wrapper.

Both input transports keep their existing framing and grammar. Character records
already within a buffer remain memory slices; cross-buffer characters use the
existing pooled spill buffer. UTF-8 syntax parsing keeps its pooled byte storage;
only requested fields need decoding. The optimization does not change public
`RecordReadResult.Record`, public parser constructors, schema behavior, position
tracking, or independently materialized strings/objects.

## Preventing recurrence

The repository's `.github/skills/file-parser-design/SKILL.md` and buffered-source
reference now document these shared helpers, explicit ownership choices, wrapper
deferral, lazy value decoding, and inspection of recent/base-class fixes before
copying another implementation. They require raw-read allocation budgets separately
from selected output strings, for sync and async transport where applicable.

`CsvStorageReuseTests` covers metadata identity, changing field counts, retained
public parser/mapper/wrapper metadata, Unicode and long/quoted records, async-only
non-seekable I/O, mixed reads, custom overrides, and raw allocation budgets for
both CSV modes. `FieldMetadataBufferTests` verifies shared publication semantics;
fixed-width tests verify that ordinary field access does not force a wrapper.

The allocation guard adds PA005 for eager reader handoffs such as
`Record = row.AsMemory()`. No new exception is needed for that rule. Three old
wrapper occurrences were removed; two reviewed constructor occurrences remain
at the explicit shared public/compatibility materialization boundary. The guard
is a syntax review trigger, not a proof of allocation behavior; the budgets and
ownership tests cover the runtime contract.

## Benchmark methodology

Each operation reads 50,000 deterministic requests and returns
`(string Request, int Status, long Bytes)` per row, immediately consumed into a
checksum. Every row is validated during setup; measured invocations check their
checksum. Input encodings and contents match the earlier twelve-case benchmark,
including the unused JSON timestamp's escaped plus sign.

All inputs are pre-encoded UTF-8 without BOM, with LF newlines. Configuration,
generation, encoding and validation are excluded. Fresh memory streams, reader
creation, parsing, typed access, projection, consumption and disposal are included.
All parsers use 4 KiB buffers, no read-ahead, no initial row count or position
tracking. Projection selects request/status/bytes without ordered or stable-shape
assumptions. Combined Log has additional referrer/user-agent fields; W3C reconstructs
the common request from method/URI. Format sizes and parsing work therefore differ.

The matrix warms each case for two seconds, then measures seven batches of at
least one second each. Full GCs before samples are excluded. `Stopwatch` measures
wall time; `Process.TotalProcessorTime` measures process CPU including GC threads;
`GC.GetAllocatedBytesForCurrentThread` measures calling-thread managed allocations.
No builds/tests run during measurement. No machine power plan is changed.

Measurements use Release, .NET 10.0.14, Windows 11 x64, Intel Core Ultra 9 275HX,
SDK 11.0.100-rc.2.26504.105, on 2026-11-01. MB here is decimal (1,000,000 bytes),
not peak live memory. `before.csv` preserves the earlier measurement on `b359baf`;
timing differences between sessions are historical evidence, not a controlled
same-process A/B test. `after.csv` contains the current twelve-case matrix.

Reproduce from this worktree's repository root:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Remove-Item Env:ParserSourceRoot -ErrorAction SilentlyContinue
dotnet build benchmarks/WebLogParserComparison/WebLogParserComparison.csproj -c Release
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --validate
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --matrix
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --filter '*WebLogParserComparison*'
```

The optional `ParserSourceRoot` MSBuild property can select a different checkout;
it is unset here so the benchmark and its generated builds use this implementation.

## Historical twelve-parser results per 50,000 records (`93a566a`)

The CPU column is process CPU from the matrix; elapsed time is reported separately.
Allocated bytes include the three requested output values. Raw values and standard
deviations are in `after.csv`.

| Parser | CPU (ms) | Wall mean (ms) | Allocated (MB) | Bytes/row |
|---|---:|---:|---:|---:|
| CSV characters | 25.425 | 25.412 | 4.370 | 87.41 |
| CSV UTF-8 | 20.772 | 20.881 | 4.366 | 87.32 |
| Fixed-width | 8.598 | 8.606 | 4.367 | 87.35 |
| NDJSON | 30.371 | 30.651 | 4.614 | 92.29 |
| JSON | 29.543 | 29.718 | 4.612 | 92.25 |
| logfmt | 16.201 | 16.214 | 4.589 | 91.79 |
| LTSV | 9.779 | 9.782 | 4.569 | 91.37 |
| Common Log | 12.181 | 12.344 | 4.502 | 90.05 |
| Combined Log | 14.896 | 14.963 | 4.561 | 91.22 |
| W3C | 11.126 | 11.124 | 7.270 | 145.40 |
| JSON projected | 19.773 | 19.747 | 4.612 | 92.24 |
| NDJSON projected | 28.699 | 28.780 | 4.614 | 92.28 |

| Changed reader | Allocated before (MB) | Allocated after (MB) | Reduction |
|---|---:|---:|---:|
| CSV characters | 29.170 | 4.370 | 85% |
| CSV UTF-8 | 29.166 | 4.366 | 85% |
| Fixed-width sync | 8.367 | 4.367 | 48% |

The CSV decrease is approximately 24.8 MB, matching removal of a seven-field
array (416 bytes) and wrapper (80 bytes) on each of 50,000 rows. The remaining
allocation is dominated by required request strings. JSON and key/value continue
to reuse storage through the shared primitive, with only negligible per-reader
setup differences. Fixed-width saves the 4 MB of synchronous wrappers.

## Independent BenchmarkDotNet check

All twelve .NET 10 ShortRun cases completed with MemoryDiagnoser. Its allocation
agrees with the matrix: CSV characters 4.17 MiB, CSV UTF-8 4.16 MiB, and fixed-width
4.17 MiB (the tables above use decimal MB). `after.bdn.md` and `after.bdn.csv`
preserve elapsed times, error estimates, GC counts, and environment details.

BenchmarkDotNet elapsed means are 25.998 ms for CSV characters and 16.352 ms for
UTF-8. The matrix CPU result for UTF-8 (20.772 ms) is higher than the historical
baseline (17.966 ms), whereas the isolated BenchmarkDotNet elapsed result is lower
than its earlier baseline (17.673 ms). Therefore these runs demonstrate a consistent
allocation reduction, not a universal CPU speedup. Separate JIT/process histories
and short-run variation affect timings; do not mix CPU and elapsed figures from
different runs to calculate utilization.

## Validation

Final Release tests on each of .NET 8, 9, and 10:

| Project | Passed per target framework |
|---|---:|
| Core | 1,113 |
| JSON | 1,155 |
| NDJSON | 310 |
| KeyValue | 77 |
| FixedWidth | 68 |
| WebLogs | 146 |
| SecurityLogs | 52 |
| Total | 2,921 |

That is **8,763 passing executions**, including 26 new regression cases per target
framework. Early focused runs exposed an unopted-in internal character-source
subclass and a fixed-width borrowed-metadata compatibility requirement; both were
corrected before this final run. An async-only stream fixture also required correction.

- Full solution Release build passed across declared target frameworks, with four
  warnings in existing test/profiler code and dependencies.
- Standalone comparison project built on .NET 8/9/10 with no warnings/errors.
- All twelve setup cases validated every returned value (600,000 row checks),
  and measured matrix invocations checked their checksums.
- Independent BenchmarkDotNet: all twelve cases completed.
- Allocation guard passed (18 reviewed occurrences); its self-tests passed 30 checks.
  The new PA005 rule needs no exception. Exceptions moved only after ownership review.
- Skill quick validation passed. Its missing PyYAML dependency was installed only
  under ignored worktree artifacts; no machine-wide configuration changed.
- `git diff --check` passed.
- Not run: the full repository suite beyond the seven projects listed, Arrow and
  Chrononuensis test projects, benchmarks on .NET 8/9, other operating systems, or
  disk-I/O performance. Cross-buffer/large records are covered by correctness and
  allocation tests; the comparative performance table is the flat log workload.
