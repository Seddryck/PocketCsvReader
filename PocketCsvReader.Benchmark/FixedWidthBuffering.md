# Fixed-width buffer reuse

Issues #373 and #374 are measured separately with the same `FixedWidthBuffering` harness.
The baseline uses production source `3b0c9dd` plus only the benchmark harness. The
parser stage implements #373 while retaining the synchronous data-reader copy.
The final stage also implements #374 with `RecordSpan.AsMemory()` at reader handoff.

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*FixedWidthBuffering*' --artifacts artifacts/fixed-width-before
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*FixedWidthBuffering*' --artifacts artifacts/fixed-width-parser
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*FixedWidthBuffering*' --artifacts artifacts/fixed-width-after
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*FixedWidthBuffering*' --artifacts artifacts/fixed-width-repeat
```

Each operation consumes 512 records from an in-memory UTF-8 input. Widths are 32
and 8,192 UTF-16 code units, with a 1,024-character parser buffer, CRLF separators,
Unicode text, and a final unterminated record. Setup validates row counts and a
typed integer checksum. `Rows` reads synchronously without materializing fields;
`Typed` additionally calls `GetInt32(0)`; `RowsAsync` uses asynchronous reads.
Input generation/encoding is excluded; initialization and disposal are included.

| Operation | Width | Baseline mean (us) | Parser mean (us) | Baseline allocated (KiB) | Parser allocated (KiB) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Rows | 32 | 62.86 | 29.51 | 178.29 | 89.91 |
| Typed | 32 | 70.13 | 36.80 | 178.78 | 90.40 |
| RowsAsync | 32 | 68.84 | 35.67 | 134.36 | 45.98 |
| Rows | 8192 | 13486.02 | 5248.36 | 24709.48 | 8249.91 |
| Typed | 8192 | 12916.11 | 5230.44 | 24709.97 | 8250.40 |
| RowsAsync | 8192 | 12564.05 | 4586.48 | 16505.55 | 45.98 |

| Operation | Width | Final mean (us) | Final allocated (KiB) |
| --- | ---: | ---: | ---: |
| Rows | 32 | 27.10 | 45.91 |
| Typed | 32 | 36.80 | 46.40 |
| RowsAsync | 32 | 48.28 | 45.98 |
| Rows | 8192 | 5288.32 | 45.91 |
| Typed | 8192 | 5442.25 | 46.40 |
| RowsAsync | 8192 | 8017.27 | 45.98 |

| Operation | Width | Final repeat mean (us) | Final repeat allocated (KiB) |
| --- | ---: | ---: | ---: |
| Rows | 32 | 37.06 | 45.91 |
| Typed | 32 | 41.30 | 46.40 |
| RowsAsync | 32 | 47.66 | 45.98 |
| Rows | 8192 | 6332.92 | 45.91 |
| Typed | 8192 | 5685.29 | 46.40 |
| RowsAsync | 8192 | 5693.02 | 45.98 |

The isolated synchronous handoff change removes 44 KiB for small records and
8,204 KiB for large records per operation (512 character arrays). Overall
synchronous row allocations fall by 74.2% and 99.8%, respectively. The final
synchronous means are 56.9% and 60.8% below baseline. Against the parser-only stage,
synchronous means change by -8.2% / +0.8% for Rows and 0.0% / +4.0% for Typed;
these short samples do not establish a material synchronous throughput regression.
The async implementation is identical between parser-only and final stages, yet
its mean increases by 35% / 75% in the first final run. Warmup/measurement drift
is visible in that run; a repeat of the identical final harness is retained below
to expose the timing variability rather than treating the first run as conclusive.
The repeat has identical allocations but materially different times, including
37.06 versus 27.10 us for small synchronous rows and 5.69 versus 8.02 ms for large
async rows. All final means in both runs remain below the original baseline,
but the runs do not reliably resolve the isolated handoff's throughput effect.

The parser returns views of the current input buffer. Records crossing refills
use one lazily rented, reused spill array bounded by the declared layout width
(rounded to the pool bucket). Only field characters need storage; trailing data
and terminators are counted/matched without retention. Both paths reuse the
precomputed field array and share framing, terminator matching, position tracking,
and validation. Borrowed records remain valid until the next read or disposal.
Synchronous reader handoff still copies at the parser stage. Async results still
allocate a `RecordMemory` wrapper per record; output strings are independently owned.

Measured on 2026-10-31, Windows 11 x64, Intel Core Ultra 9 275HX, .NET 10.0.14,
SDK 11.0.100-rc.2.26504.105, BenchmarkDotNet 0.15.8, concurrent workstation GC.
One launch, three warmups, three measured iterations; power plan unchanged.
These are short local timing samples with broad confidence intervals, not a stable
throughput guarantee. Disk I/O and other parsers/benchmark classes were not measured.
Full errors, standard deviations, and GC statistics are preserved in
[baseline Markdown](FixedWidthBuffering.before.md), [baseline CSV](FixedWidthBuffering.before.csv),
[parser Markdown](FixedWidthBuffering.parser.md), and [parser CSV](FixedWidthBuffering.parser.csv).
Final measurements are in [final Markdown](FixedWidthBuffering.after.md) and
[final CSV](FixedWidthBuffering.after.csv); the unchanged final repeat is in
[repeat Markdown](FixedWidthBuffering.repeat.md) and [repeat CSV](FixedWidthBuffering.repeat.csv).
All six cases completed at each of the four measurement stages (24 measurements).

Validation: focused buffer/parser tests and handoff tests ran first. The final
66-case `PocketCsvReader.FixedWidth.Testing` suite passed on each of .NET 8, 9,
and 10 (198 final executions), including Unicode, split/custom terminators,
bounded long tails, malformed widths, headers/padding, non-seekable async-only
streams, source positions, storage reuse, alternating sync/async reads, EOF,
and early/repeated disposal. `dotnet build PocketCsvReader.full.sln -c Release`
succeeded with zero errors and seven unrelated compression-test/profiler warnings.
Other test projects and benchmark classes were not run.

The source allocation guard and its 26 self-tests pass. Its obsolete fixed-width
copy/materialization exceptions were removed. The sole replacement exception was
reviewed against the internal `ReadOnlyMemory<char>` constructor: async results
require a `RecordMemory` wrapper, but borrow the existing characters and field
array without copying. Ownership tests and four warmed raw-read allocation cases
cover both transports and both benchmark widths. The budget allows 96 bytes per
row plus 1 KiB slack after initialization/pool warmup, enough for the current
80-byte x64 wrapper but below a wrapper plus even one 32-character array per row.
This CI repair changes no production code; the benchmark reports above remain
measurements of the current implementation and were not rerun for this repair.
