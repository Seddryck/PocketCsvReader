# Web-log allocation follow-up after rebasing PR #393

Rebased onto `origin/main` at `f38c9a1` (analyzer policy and pre-push checks,
PR #394). Conflicts preserved main's generic type naming and scanner cleanup.
The earlier controlled CSV CPU experiments remain historical measurements against
`b359baf`; rebasing does not turn them into measurements of the new base.

## Findings and change

W3C synchronous parsing already reused its schema array and memory-backed frames
after #376. It did not have CSV's per-row field-array allocation. Its async
reader, like Common and Combined, still constructed an 80-byte `RecordMemory`
wrapper per row: **4,000,000 bytes per 50,000 records**.

All three formats now opt into the existing `ICurrentRecordSource` transport.
Their async data-reader path stores `CurrentRecord` directly. The synchronous
reader keeps its existing direct source path. Public `ReadAsync` still returns
the required `RecordReadResult.Record` wrapper at the explicit materialization
boundary. Character memory and field metadata remain borrowed until advancement;
materialized strings remain independently owned. The obsolete source-guard
exception for the web-log wrapper is removed, with no new exception.

W3C's larger synchronous projection allocation is a different cost. The workload
stores `/items/...` in `cs-uri-stem`, then reconstructs `GET /items/... HTTP/1.1`.
That requires a URI string from `GetString` followed by the final request string.
The raw parser does not allocate either of those strings or eagerly decode them.
Changing those returned values would change the comparison's three-value output.

## Measurement

The before capture uses the rebased PR code at `0aa9383` (CSV optimizations already
applied), plus the diagnostic harness. The after capture uses this PR's web-log
transport update with the same workload and measurement loop. Run the harness:

```powershell
dotnet build benchmarks/WebLogParserComparison/WebLogParserComparison.csproj -c Release -f net10.0
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --weblog-breakdown
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --matrix
```

The breakdown uses 50,000 records, 4 KiB buffers, input generation and validation
outside timing, one second of warmup and three batches lasting at least one
second each. Each batch reports actual process CPU and current-thread allocated
bytes per complete pass, with GC outside timing. `MemoryStream` completes async
I/O inline; the async breakdown exercises reader/source state machines without
disk or scheduler latency. Existing async-only stream tests separately cover
asynchronous I/O. Raw reads request no values; numeric reads request status and
bytes; string reads add the stored request/URI; projection reconstructs W3C's
request. All twelve matrix cases validate every three-value tuple before timing.

These sequential runs diagnose allocation ownership. Their short CPU samples
are observational, without alternating fresh-process pairs or affinity controls;
they do not establish a CPU speedup. See `CONTROLLED-CSV.md` for that separate,
controlled CSV experiment.

Allocated decimal MB per 50,000 records (including reader initialization):

| Format / path | Before | After | Saved |
|---|---:|---:|---:|
| Common raw, sync | 0.140 | 0.140 | 0 |
| Common raw, async | 4.140 | 0.140 | 4.000 |
| Common three-value projection, async | 8.502 | 4.502 | 4.000 |
| W3C raw, sync | 0.108 | 0.108 | 0 |
| W3C raw, async | 4.108 | 0.108 | 4.000 |
| W3C three-value projection, async | 11.270 | 7.270 | 4.000 |

For W3C synchronous reads, numeric access totals 0.109 MB, adding the URI string
totals 2.910 MB, and reconstructing the request totals 7.270 MB. The final request
strings account for 4.360 MB and the intermediate URI strings for about 2.801 MB.
This explains the higher projection allocation without implicating lazy parsing.
Common already stores the complete request, so its projection needs one string.

Raw CSV data are in `weblog-before.csv` and `weblog-after.csv`; each includes the
observational process CPU measurements as well as allocated bytes. The complete
final twelve-parser matrix is in `rebased-matrix.csv`.

## Validation on the rebased implementation

Focused `CurrentRecordViewTests`: 15 cases passed on each of .NET 8, 9, and 10
before broader validation. They cover all three formats, sync and async raw-read
budgets, wrapper absence, mixed reads, retained strings, and public async source
wrapper/borrowed-array compatibility. Existing web-log buffering tests cover tiny
and cross-buffer records, long Unicode fields, async-only non-seekable input,
directives, schema errors, EOF and malformed input.

Complete affected-project runs passed on **each** of .NET 8, 9, and 10:

| Project | Passed per framework |
|---|---:|
| Core | 1,114 |
| JSON | 1,155 |
| NDJSON | 310 |
| KeyValue | 77 |
| FixedWidth | 68 |
| WebLogs | 161 |
| SecurityLogs | 52 |
| Total | 2,937 |

**8,811 passing executions**, no failures or skips. Full solution Release build
passed; the final incremental build reported three existing profiler dependency
warnings. The benchmark project built for all three frameworks with zero warnings
and errors. The new main-branch analyzer/pre-push verification passed. Allocation
guard: 30 self-tests and 17 reviewed occurrences passed. Skill quick validation
and `git diff --check` passed. The skill now explicitly requires checking both
transports and separating reconstruction/output strings from parser overhead.

Not run: Arrow and Chrononuensis tests, the complete repository test suite beyond
the seven projects above, a new BenchmarkDotNet run, fresh controlled CSV CPU
pairs, benchmarks on .NET 8/9, other operating systems or disk-I/O performance.
The prior controlled CSV results remain available with their original revisions
and controls in `CONTROLLED-CSV.md`.

## Final twelve-parser matrix

Synchronous three-value projection, .NET 10.0.14; seven batches after warmup.
CPU is process CPU per 50,000 rows. Allocation includes required output strings.

| Parser | CPU ms | Allocated MB |
|---|---:|---:|
| CsvChars | 33.040 | 4.370 |
| CsvUtf8 | 22.921 | 4.366 |
| FixedWidth | 12.682 | 4.367 |
| Ndjson | 42.062 | 4.614 |
| Json | 38.277 | 4.612 |
| Logfmt | 18.532 | 4.589 |
| Ltsv | 15.026 | 4.569 |
| CommonLog | 16.356 | 4.502 |
| CombinedLog | 19.734 | 4.561 |
| W3c | 15.844 | 7.270 |
| JsonProjected | 30.554 | 4.612 |
| NdjsonProjected | 33.222 | 4.614 |
