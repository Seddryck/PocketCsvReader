# JSON and NDJSON allocation investigation

## Cause

Both readers use `JsonRecordParser` for ordinary, unprojected rows. Each seven-field
object previously allocated a fresh `List<FieldSpan>`, its initial four-element
backing array, its grown eight-element array, and a final seven-element array.
On this x64 runtime `FieldSpan` is 56 bytes, so those allocations are:

| Allocation | Bytes per row |
|---|---:|
| List object | 32 |
| Four-field backing array | 248 |
| Eight-field backing array | 472 |
| Seven-field result array | 416 |
| Total metadata | **1,168** |

The isolated parser test measured 1,168,000 bytes over 1,000 unescaped seven-field
objects. Across the original 50,000-row workload this accounts for **58.4 MB** of
the approximately 77.4 MB allocation. Property-name strings are already cached by
the reader's row-shape cache; recreating names is not the main cost for this input.
The key/value reader already reuses its scratch list and returned field array;
the ordinary JSON parser was recreating that storage for each row instead.

There is a second cost in the benchmark data: `System.Text.Json` serializes the
timestamp's plus sign as `\u002B`. The ordinary parser eagerly decodes every escaped
value, even when the caller only reads request, status, and bytes. A new
`StringBuilder`, its backing storage, and the decoded timestamp add about
288 bytes/row, or 14.4 MB/input. Changing only that escape to a literal `+` in the
baseline reduces allocation from 77.41 MB to 63.00 MB. JSON and NDJSON share these
costs, explaining their almost identical allocation totals despite different
input framing. This analysis concerns the synthetic flat log workload; nested
documents can have additional metadata costs.

## Implementation

Ordinary streaming reads now keep a reader-owned `JsonFieldBuffer`. It reuses the
root scratch list and exact-length result array while the field count is unchanged.
All labels and values are still parsed, so properties may be renamed, reordered,
added, or removed. Metadata is committed only after parsing and trailing-content
validation succeed; scratch references are cleared on both success and failure.
The borrowed metadata has the same lifetime as the buffered character input:
until the next read on that reader.

The buffer also reuses escape-decoding `StringBuilder` storage. Decoded strings
remain independently owned, preserving existing string and nested-field behavior.
An unusually large decoding buffer (over 4 KiB capacity) is replaced before the
next escaped string so a single large value does not leave that buffer retained
through subsequent small values.

Record-path selection keeps independently owned metadata because it holds a
parsed tree while enumerating matches. Optional stable-shape and projection paths
retain their existing behavior. Nested object/array metadata still allocates;
this change targets the dominant repeated root metadata and decoding workspace.

## Existing option for three-value workloads

When the caller needs only the three returned properties, configure projection:

```csharp
var reader = new JsonReaderBuilder()
    .WithProjection(p => p.Property("request").Property("status").Property("bytes"))
    .Build();
```

`NdjsonReaderBuilder` supports the same call. Projection avoids materializing the
unused timestamp and other unused fields while still validating the input. It
already existed before this optimization and is measured separately below.
No ordered-projection or stable-shape assumption is enabled by the new benchmark.

## Measurement

`JsonAllocationComparison` reproduces the previous workload: 50,000 deterministic
seven-property UTF-8 records, projected to `(string Request, int Status, long Bytes)`
and immediately consumed in a checksum. Every output row is validated before timing.
It measures both JSON array and NDJSON inputs, with and without the timestamp escape,
and with ordinary parsing versus explicit three-property projection.

Input generation, serialization, UTF-8 encoding, configuration, and validation are
excluded. Fresh read-only memory streams, reader creation, parsing, typed access,
projection, checksum consumption, and disposal are included. Parsers use 4 KiB
buffers, LF newlines, no read-ahead, no initial row count, and no position tracking.

The baseline is `d404b26`; the identical benchmark source was compiled in a local
probe against its unchanged JSON/NDJSON projects. Baseline and optimized matrix
runs were sequential, without concurrent builds or tests. The machine is an
Intel Core Ultra 9 275HX, Windows 11, x64, .NET 10.0.14; the SDK is
11.0.100-rc.2.26504.105. Measurements were taken on 2026-11-01.

Each case warms up for at least two seconds and measures seven samples of at least
one second each. `Stopwatch` measures wall time, `Process.TotalProcessorTime`
measures CPU time including process threads, and
`GC.GetAllocatedBytesForCurrentThread` measures synchronous managed allocation.
A full GC before each sample is excluded. CPU figures have Windows timer
quantization error; timing rankings are machine- and workload-specific.
MB in this report is decimal (1,000,000 bytes), and means cumulative allocation,
not peak live memory or working set.

Reproduce the optimized run from the repository root:

```powershell
dotnet build PocketCsvReader.Benchmark/PocketCsvReader.Benchmark.csproj -c Release -f net10.0
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --json-allocation-matrix
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --filter '*JsonAllocationComparison*'
```

## Results

For the original escaped-timestamp workload, ordinary parsing now allocates
**88.9% less**. CPU time improved by about **22% for JSON** and **25% for NDJSON**
in the sequential matrix runs:

| Format | CPU before (ms) | CPU after (ms) | Allocated before (MB) | Allocated after (MB) |
|---|---:|---:|---:|---:|
| JSON | 40.038 | 31.115 | 77.411 | 8.613 |
| NDJSON | 44.925 | 33.705 | 77.413 | 8.615 |

The allocation decrease is approximately 1,376 bytes/row: 1,168 bytes of metadata
and 208 bytes of decoding workspace. The decoded timestamp still needs about
80 bytes/row, even though this particular workload does not use it.

The controls show the remaining costs and the existing projection option:

| Format | Ordinary, literal timestamp (MB) | Ordinary, escaped timestamp (MB) | Projected, escaped timestamp (MB) |
|---|---:|---:|---:|
| JSON baseline | 63.004 | 77.411 | 4.612 |
| JSON optimized | 4.606 | 8.613 | 4.612 |
| NDJSON baseline | 63.005 | 77.413 | 4.614 |
| NDJSON optimized | 4.607 | 8.615 | 4.614 |

Projection was already efficient; its allocation remains essentially unchanged.
For this use case, configuring projection reduces allocation by about **94%**
compared with the original ordinary-parser result. The remaining ordinary-parser
cost with literal timestamps is mostly the returned request strings and the
per-row record wrapper, plus per-input setup and framing costs. Lazily decoding
unrequested escaped fields could reduce the remaining default-path cost further,
but would require changes to value-access behavior; projection already avoids
that work when the required columns are known.

Raw samples are in `JsonAllocationComparison.before.csv` and `.after.csv`.
All numbers above are per 50,000 records. The source inputs have the same seven
properties; only timestamp escaping and output projection differ in the controls.

An independent BenchmarkDotNet short run completed all eight optimized scenarios.
For the escaped ordinary-parser cases it measured 31.37 ms elapsed for JSON and
32.71 ms for NDJSON, with allocation agreeing with the matrix. Its generated
`JsonAllocationComparison.bdn.md` and `.bdn.csv` contain uncertainty and GC data.
BenchmarkDotNet labels binary mebibytes as "MB", whereas this report uses decimal
MB. Its elapsed times are not process CPU times; do not combine figures from the
independent runs to calculate utilization.

## Validation performed

- JSON tests: **895 passed on each of .NET 8, .NET 9, and .NET 10**, including
  eight new buffer/allocation regression cases.
- NDJSON tests: **302 passed on each of .NET 8, .NET 9, and .NET 10**.
- Total: **3,591 passing test executions** in the final cross-framework run.
- The existing suites cover asynchronous reads, changing object shapes, nested
  values, record-path selection, stable-shape parsing, projection, JSONC, escaped
  strings, and input crossing buffer boundaries. New tests specifically cover
  allocation after warmup, decoding workspace reuse, unchanged published metadata
  after malformed input, clearing scratch references, and empty-object allocation.
- Baseline and optimized eight-case matrices: every projected row validated and
  checksums checked during measurement.
- Independent BenchmarkDotNet optimized run: all eight cases completed.
- Full Release solution build: succeeded across declared .NET 8/9/10 targets;
  warnings remain in existing core tests/profiler dependencies and generated code.
- `git diff --check`: passed.
- Not run: the full repository test suite, unrelated parser test projects,
  benchmarks on .NET 8/9, other operating systems, or a nested-document performance
  matrix. Improvements quoted here apply to the measured flat log workload.
