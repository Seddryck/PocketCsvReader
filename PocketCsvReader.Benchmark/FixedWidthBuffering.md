# Fixed-width buffer reuse

Issues #373 and #374 are measured separately with the same `FixedWidthBuffering` harness.
The baseline uses production source `3b0c9dd` plus only the benchmark harness. The
parser stage implements #373 while retaining the synchronous data-reader copy.

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*FixedWidthBuffering*' --artifacts artifacts/fixed-width-before
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*FixedWidthBuffering*' --artifacts artifacts/fixed-width-parser
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
