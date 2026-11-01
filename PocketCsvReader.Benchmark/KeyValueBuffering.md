# LTSV and logfmt buffered record allocation comparison

Run from the repository root:

```powershell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --keyvalue-buffering-matrix
```

Measured on Windows x64, .NET 10 (SDK 11.0.100-rc.2.26504.105), comparing
`3b0c9dd` with this implementation. The identical harness was added before
changing the parser and run on both versions. The table was re-measured after
the CI complexity refactor: the original parser and record-source files were
temporarily restored from `3b0c9dd` for the before build, then the implementation
was restored for the after build. Both runs used identical harness and core
sources. Raw outputs are retained in `KeyValueBuffering.before.csv` and
`KeyValueBuffering.after.csv`. Allocations reproduced the initial measurements;
elapsed times vary with host load.

Each operation creates a memory stream and data reader, consumes 512 records,
reads the integer ID and materializes the message string, then disposes the
reader. Input generation is outside measurement. The harness checks both the
record count and value checksum. It uses a 2-second warmup per case, then seven
samples of at least 200 ms, collecting between samples. Allocations use
`GC.GetAllocatedBytesForCurrentThread`; results include setup and required public
string outputs. Times are mean +/- sample standard deviation, in microseconds
per operation. These are local comparative measurements, not throughput promises.

All cases have three fields, Unicode messages, CRLF terminators, and a configured
4096-character buffer. Simple messages are `東京 café`. Escaped messages append
literal JSON escape text (`\n\"ok\"`): logfmt decodes this text, while LTSV
preserves it literally as required by its grammar. CrossBuffer messages contain
8192 `x` characters, so every record spans multiple buffers.

| Format | Case | Before us | After us | Time after/before | Before bytes | After bytes | Allocation reduction |
|---|---|---:|---:|---:|---:|---:|---:|
| LTSV | Simple | 68.68 +/- 8.85 | 51.36 +/- 7.51 | 0.748 | 354,656 | 27,928 | 92.1% |
| LTSV | Escaped | 84.26 +/- 1.78 | 64.31 +/- 0.34 | 0.763 | 379,232 | 36,240 | 90.4% |
| LTSV | CrossBuffer | 4364.46 +/- 65.87 | 3236.58 +/- 87.77 | 0.742 | 25,499,928 | 8,469,352 | 66.8% |
| logfmt | Simple | 88.93 +/- 1.19 | 65.58 +/- 1.18 | 0.737 | 358,752 | 27,568 | 92.3% |
| logfmt | Escaped | 155.16 +/- 3.19 | 117.06 +/- 1.83 | 0.754 | 432,480 | 56,720 | 86.9% |
| logfmt | CrossBuffer | 4187.38 +/- 176.51 | 3265.01 +/- 80.58 | 0.780 | 25,499,928 | 8,469,352 | 66.8% |

The contained-record path borrows the shared character buffer. Records crossing
buffers use the existing shared pooled capture/coalescing path, released at the
next read or disposal. Temporary field lists retain their capacity; the source
reuses the returned metadata array while the field count stays the same and
replaces it when the count changes. It does not keep a cache of arrays for every
field count. Both character views and metadata follow the current-record lifetime.
Materializers still return independently owned results.

Whole-record line strings, public-constructor character copies, and per-record
field lists/arrays are removed from the steady-state path. Escaped logfmt values
still allocate decoded strings; public message materialization also remains in
these measurements. The JSON decoder now consumes a span, avoiding its former
quoted-value substring allocation.

Timing/allocation measurements here cover synchronous reads. Asynchronous reads
use the same framing and grammar with asynchronous refills; async-only stream
behavior, boundary handling, cancellation before reading, disposal, and retained
materialized outputs are covered by the focused KeyValue tests. No full
BenchmarkDotNet run or asynchronous timing matrix was run.
