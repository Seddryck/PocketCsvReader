# Shared character parsing core

Run the character-source comparison with:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*CharacterRecordParsing*' --warmupCount 3 --iterationCount 5 --launchCount 1
```

Each operation reads 128 two-column records through `RecordParser`, using a 1,024-character input buffer without read-ahead. The first field contains 32, 4,096, or 32,768 ASCII characters. Input creation is outside measurement. The sync and async methods consume field metadata without string materialization. This isolates parser/refill overhead and continuation allocation; it does not measure disk throughput or header/initialization costs.

The baseline is commit `dfad1c1` with this benchmark harness added, before the shared-core refactor. Measurements used BenchmarkDotNet 0.15.8, Windows 11 x64, .NET 10.0.12, and Intel Core Ultra 9 275HX on 2026-10-22. Both runs used one launch, three warmups, and five requested measured iterations.

| Method | Field characters | Before time | After time | Before allocated | After allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| Sync | 32 | 31.75 μs | 27.96 μs | 30.06 KB | 22.34 KB |
| Async | 32 | 36.07 μs | 30.49 μs | 40.13 KB | 32.41 KB |
| Sync | 4,096 | 1,587.29 μs | 991.58 μs | 3,481.58 KB | 22.34 KB |
| Async | 4,096 | 1,597.91 μs | 1,007.35 μs | 3,491.65 KB | 32.41 KB |
| Sync | 32,768 | 17,519.64 μs | 7,785.51 μs | 141,129.58 KB | 22.34 KB |
| Async | 32,768 | 18,175.39 μs | 8,087.19 μs | 141,139.65 KB | 32.41 KB |

The small-record samples took about 12–15% less time, and the largest samples about 56% less. Long-record allocation falls because completed prefixes are no longer concatenated into a fresh array on every refill. These short local measurements are comparative evidence; small timing differences need longer runs on the intended workload.

## Ownership and shared lifecycle

- Character CSV syntax remains in the existing field state machine. The character scanner feeds it buffered characters and handles record/comment/EOF transitions and bad-data recovery. The UTF-8 scanner remains separate and retains its byte bulk scanning and lazy decoding.
- Synchronous and asynchronous character methods feed one scanner. Their only transport difference is `IBufferReader.Read` versus `ReadAsync`; the scanner contains no I/O or sync/async mode switch. The current record's continuation length, field count, and malformed-record recovery state survive refills in a value-type context.
- `SingleBuffer` or `DoubleBuffer` owns the input arrays. A record contained in the current input buffer returns a memory slice directly, including a final unterminated record. It does not pass through a copying span constructor or full-record string.
- A record crossing buffers appends consumed chunks into a lazily allocated parser-owned continuation buffer. Its final size is unknown while streaming, so geometric capacity growth can relocate accumulated data when capacity is exhausted. Chunks are otherwise appended once, without a final consolidation copy or a new full-prefix allocation for every refill. Storage is reused for later records and remains bounded by the largest active record and input buffers.
- Record memory stays valid until the next read or disposal. The configured character pool receives continuation arrays on growth/disposal. Field metadata is copied per returned record; field text stays in parser-owned character memory.
- Header selection, field-string extraction, and collection are shared across byte/character sources. Existing virtual character-parser header hooks still dispatch normally. Returned header strings are required by the public API and are materialized before advancing the source.
- Reader initialization shares explicit-encoding setup, decompression selection, character-reader construction/BOM positioning, and initialized-state setup. Sync/async wrappers retain their corresponding probing and source-creation I/O. Character initialization no longer needs a one-character warm-up read followed by a rewind. Character streams retain the matching preamble for `StreamReader` to consume exactly once, preserving a following literal U+FEFF in field data; byte sources consume their own BOM before scanning.

Focused tests cover zero-copy input views, streaming the first record without consuming the document, buffer growth/pool returns, mixed reads, EOF record memory, bad-data actions/diagnostics, multi-row headers, virtual overrides, and async-only refills. Source-parity tests cover UTF-8 and character modes; extension reader tests exercise the shared initialization.
