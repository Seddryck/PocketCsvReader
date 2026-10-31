# CSV and Common Log Format comparison

Run from the repository root:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*CommonLogComparison*'
```

This compares this repository's `CsvReader` with `CommonLogReader`, rather than a third-party CSV library. Each operation reads a fresh memory stream containing 10,000 or 100,000 equivalent seven-field records. Input generation and UTF-8 encoding occur outside measurement. Both profiles use explicit UTF-8, forward-only initialization, a 4,096-character parser buffer setting, no row pre-counting, no position tracking, and default string materialization. Their internal buffering and grammar implementations differ. CSV uses the decoded-character path, not the optional native UTF-8 path.

The generated workload varies hosts, users, requests, status codes, response sizes, and two timestamp offsets. Identity is missing throughout; user and byte-count fields include missing values. Requests contain spaces and are quoted in both formats. This workload does not cover escaped quotes, very long requests, malformed input, or Combined Log Format.

Two groups separate costs:

- **Rows:** call `ToDataReader()` and `Read()` until EOF, returning the row count without extracting field values. This measures initialization, framing, field discovery, and disposal.
- **Typed:** create and consume `CommonLog` records. CLF calls `ToCommonLog()`; CSV uses an explicit streaming mapper with equivalent nullable fields and the same invariant span-based timestamp and numeric conversions. This avoids comparing CLF's explicit mapper against a reflection-based CSV object mapping path. A checksum consumes all seven properties without retaining the records.

Setup checks every typed record for equality, separately checks timestamp offsets, verifies both row counts, and compares typed checksums before any timing. Filesystem I/O, data generation, and correctness checks are excluded. Stream/reader initialization, typed record allocation, and disposal are included in each operation.

## Original local measurements

These measurements predate the web-log buffer reuse change. See [the before/after buffering comparison](WebLogBuffering.md) for fresh measurements on the optimized implementation.

Measured on 2026-10-31 against production source `3257b6b` with this benchmark added: BenchmarkDotNet 0.15.8, Windows 11 x64, Intel Core Ultra 9 275HX, .NET 10.0.14, concurrent workstation GC. The installed build SDK was 11.0.100-rc.2.26504.105. One process launch, three warmup iterations, and three measured iterations were used per case; the benchmark runner leaves the machine power plan unchanged.

| Work | Rows | CSV mean | CLF mean | CSV allocated | CLF allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| Row parsing | 10,000 | 5.207 ms | 2.260 ms | 4.74 MiB | 12.89 MiB |
| Row parsing | 100,000 | 52.518 ms | 23.745 ms | 47.31 MiB | 128.94 MiB |
| Typed records | 10,000 | 13.447 ms | 9.745 ms | 8.11 MiB | 16.26 MiB |
| Typed records | 100,000 | 126.645 ms | 101.999 ms | 80.99 MiB | 162.61 MiB |

Each time and allocation value is for the complete input, not one row. Allocated memory is cumulative managed allocation, not peak live memory. Input sizes are 873,840 / 893,840 bytes for 10,000 CSV / CLF rows and 8,809,153 / 9,009,153 bytes for 100,000 rows.

At 100,000 rows, CLF took about 55% less time for row parsing and 19% less time for typed materialization in this run, while allocating about 2.73 times and 2.01 times as much respectively. The simpler fixed CLF grammar plausibly explains some timing advantage; that is an inference, not a profiling result. Source inspection shows CLF materializes each line, allocates a field list and array, and copies record characters through the public `RecordSpan` constructor. These are candidates for explaining its extra allocations; this benchmark does not attribute allocation to individual call sites.

These are short local measurements, not a general ranking of CSV versus CLF. Timing confidence intervals are broad with three samples (see the full report), particularly for the 10,000-row typed case. Longer runs on representative logs are needed before treating the percentages as stable. No parser optimization is included in this change.

Full BenchmarkDotNet statistics, including errors, standard deviations, GC counts, and allocation ratios, are retained in [the measured report](CommonLogComparison.measured.md) and [CSV export](CommonLogComparison.measured.csv).
