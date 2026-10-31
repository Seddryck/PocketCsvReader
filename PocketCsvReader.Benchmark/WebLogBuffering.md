# Web-log buffer reuse

This comparison measures the CLF/W3C allocation improvements for issue #371. Both runs use the same harness and inputs. The baseline is production source `62768ab` with the benchmark harness added; the optimized run includes the web-log changes in this PR. The CSV implementation is unchanged between runs.

Run from the repository root:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*CommonLogComparison*' '*WebLogBuffering*' --artifacts BenchmarkDotNet.Artifacts/after
```

## CSV/CLF comparison

`CommonLogComparison` is the unchanged harness from #369: equivalent seven-field CSV/CLF inputs of 10,000 and 100,000 records, held in memory, with explicit UTF-8, forward-only initialization, and a 4,096-character parser buffer setting. Setup validates every typed record, timestamp offset, row count, and typed checksum before timing. The two groups separate row parsing from complete typed `CommonLog` materialization with equivalent invariant conversions. Input generation and encoding are excluded; reader initialization and disposal are included.

For 100,000 records:

| Operation | CSV before | CSV after | CLF before | CLF after |
| --- | ---: | ---: | ---: | ---: |
| Row parsing time | 50.142 ms | 46.766 ms | 23.543 ms | 16.433 ms |
| Row parsing allocated | 47.31 MiB | 47.31 MiB | 128.94 MiB | 0.255 MiB |
| Typed records time | 97.934 ms | 99.357 ms | 91.243 ms | 66.551 ms |
| Typed records allocated | 80.98 MiB | 80.99 MiB | 162.61 MiB | 33.93 MiB |

CLF row-parsing allocations decreased by approximately 99.8%, and typed allocations by approximately 79.1%. The sample mean times decreased by about 30% and 27%, respectively. The unchanged CSV results also show timing variation between runs; these are short local measurements rather than proof of a stable throughput gain.

The complete 10,000-row and 100,000-row statistics are preserved in [the baseline report](WebLogBuffering.CommonLogComparison.before.md) and [optimized report](WebLogBuffering.CommonLogComparison.after.md), with [baseline CSV](WebLogBuffering.CommonLogComparison.before.csv) and [optimized CSV](WebLogBuffering.CommonLogComparison.after.csv) exports.

## Small and cross-buffer web-log records

`WebLogBuffering` consumes 512 CLF or W3C records per operation. Paths contain a slash followed by 32 or 8,192 characters, so the larger records cross multiple default 4,096-character buffers. The W3C input starts with a seven-field `#Fields` directive. Typed CLF output uses `ToCommonLog()`; W3C uses an explicit mapper to the same record type with an ISO timestamp conversion. Setup verifies row counts and a typed checksum, and the typed consumer checks the timestamp offset, user, and missing identity. These format-specific workloads compare each implementation against its own baseline, not CLF and W3C grammar against each other.

| Format | Path characters after slash | Output | Before mean | After mean | Before allocated | After allocated |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| CLF | 32 | Rows | 148.5 us | 76.29 us | 681.63 KiB | 7.43 KiB |
| CLF | 32 | Typed | 407.1 us | 344.97 us | 861.80 KiB | 187.60 KiB |
| CLF | 8,192 | Rows | 2,517.7 us | 1,280.19 us | 17,001.63 KiB | 66.14 KiB |
| CLF | 8,192 | Typed | 3,142.5 us | 2,116.97 us | 25,341.80 KiB | 8,406.31 KiB |
| W3C | 32 | Rows | 209.1 us | 62.80 us | 822.31 KiB | 7.62 KiB |
| W3C | 32 | Typed | 467.6 us | 302.86 us | 1,002.48 KiB | 187.79 KiB |
| W3C | 8,192 | Rows | 2,684.7 us | 1,584.09 us | 17,142.31 KiB | 66.56 KiB |
| W3C | 8,192 | Typed | 3,590.1 us | 2,491.61 us | 25,482.48 KiB | 8,406.73 KiB |

Full errors, standard deviations, and GC statistics are retained in [the baseline report](WebLogBuffering.WebLogBuffering.before.md) and [optimized report](WebLogBuffering.WebLogBuffering.after.md), with [baseline CSV](WebLogBuffering.WebLogBuffering.before.csv) and [optimized CSV](WebLogBuffering.WebLogBuffering.after.csv) exports.

## Ownership and remaining allocations

- The existing shared `BufferedRecordReader` identifies LF, CRLF, and CR boundaries and owns pooled input segments. Web-log syntax remains in `PocketCsvReader.WebLogs` and operates on spans.
- A record in one input buffer returns a memory view without copying its characters. A record crossing buffers retains its input segments and consolidates them once into a pooled record buffer. The next read or disposal releases that storage. The shared cursor can allocate segment bookkeeping on this slow path; memory remains bounded by the active record and input buffers.
- CLF has one reusable seven-element field array. W3C allocates the field array when its schema is established and reuses it for subsequent data rows. Character memory and field metadata are valid until the next read or disposal.
- The synchronous data reader stores the memory view directly and reads fields from it without eagerly allocating a `RecordMemory` wrapper. The asynchronous result contract still carries a small `RecordMemory` object per data row. Both transports use the same framing and field grammar.
- W3C directives retain their public string metadata. Materialized strings and typed records still allocate the independently owned output values callers can keep after advancing the reader. This explains most remaining typed-output allocations; the benchmark does not claim a fully allocation-free reader.

## Environment and validation

Measured on 2026-10-31 with BenchmarkDotNet 0.15.8, Windows 11 x64, Intel Core Ultra 9 275HX, .NET 10.0.14, concurrent workstation GC, and SDK 11.0.100-rc.2.26504.105. Each case used one launch, three warmups, and three measured iterations; the machine power plan was left unchanged. Several timing confidence intervals are broad, especially for the long-record typed cases. Allocation comparisons are more decisive than these short timing samples. Disk I/O, third-party CSV parsers, and Combined Log Format are outside this measurement.

All 16 cases completed both before and after. All 115 WebLogs tests passed on each of .NET 8, 9, and 10 (345 executions), including 81 new buffering cases. The full solution built with zero errors and seven warnings in unrelated compression tests and profiler references/entry point. Other test projects and benchmark classes were not run.
