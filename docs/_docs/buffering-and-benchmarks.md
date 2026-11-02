---
title: Buffering, allocations, and benchmarks
tags: [performance]
---

Performance depends on input width, nested data, requested fields, output allocations, encoding, record size, and I/O. A throughput or allocation result from one workload is not a universal guarantee.

## Input buffering

JSON, NDJSON, fixed-width, key-value, and log readers use reusable storage in their current implementations. Records fitting a chunk can borrow input memory; records crossing chunks require consolidation in pooled storage. CSV character and eligible native UTF-8 paths also reuse input and field storage. Materializing returned strings, objects, dictionaries, or tables still costs memory.

## Lazy JSON decoding

JSON/NDJSON reading records field positions and validates syntax. Ordinary string escape decoding is deferred until the field is requested; decoded values can be cached for that record. Ignoring a string field therefore avoids its getter materialization. [Projection](../json-projection/) goes further by avoiding metadata/nested field trees for unselected properties. These are distinct savings.

## Labeled row shapes

The shared labeled-reader layer caches repeated label sequences and ordinal dictionaries, up to 64 shapes per reader. Changing labels, order, or field count creates another shape. Do not turn this internal cache into an application assumption that every row has the same schema.

## Key-value records

LTSV/logfmt frame lines over reusable chunk buffers and reuse scratch and published field metadata. They do not create a complete line string per record. Cross-chunk records and decoded logfmt escapes can require work/storage; returned output strings still allocate as required. Synchronous and asynchronous reads share framing and parsing logic.

## Record lifetimes

Borrowed parser character/byte memory and field metadata expire on the next read or disposal. Custom span parsers and mappers must finish synchronously and copy/parse retained values. Ordinary returned strings and row objects remain usable. Array/dictionary/table materialization has its own ownership costs. See [stream lifetimes](../stream-lifetimes/) and [Arrow ownership](../apache-arrow/).

## Benchmarks

Measure Release builds with representative data, the actual fields read, and warmup. Separate raw parsing from returned-output allocations, and distinguish throughput from allocated bytes. Async allocation budgets are configuration-sensitive; Debug results do not establish Release behavior.

The repository retains reproducible projects, environment details, and measurements:

- [UTF-8 input comparisons](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/Utf8Input.md).
- [Lazy JSON decoding](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/LazyJsonDecodingComparison.md).
- [Key-value buffering](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/KeyValueBuffering.md).
- [Position-tracking measurements](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/PositionTracking.md).
- [NDJSON projection methodology](https://github.com/Seddryck/PocketCsvReader/blob/main/benchmarks/NdjsonProjection/README.md).
- [Chrononuensis measurements](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/ChrononuensisParsing.cs).

For example, run the existing benchmark project in Release with `--filter '*Utf8Input*'` or `--filter '*CustomParsing*'`. Record hardware/runtime, warmup, job settings, and materialization work when comparing results. The current benchmark runner preserves the machine power plan.
