---
title: Use sparse indexes
tags: [usage]
---

A sparse index records a key and the byte offset of its record at regular intervals. These entries are called checkpoints. Because the records are sorted, PocketCsvReader can binary-search the checkpoints, seek to the last checkpoint with a key strictly below the target, and scan until the key becomes greater. If there is no lower checkpoint, it starts at the first data record. Starting below the target preserves all matches when duplicate keys span several checkpoints.

## Configure the reader and index

Add `PocketCsvReader.Indexing`. This example assumes `orders.csv` has LF line endings, a header with `Date` and `OrderId` columns, and records sorted by ascending date.

```csv
Date,OrderId
2026-05-16,16
2026-05-17,17
2026-05-17,18
2026-05-18,19
```

```csharp
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .Build();
var dateIndex = new SparseIndex(new IndexKey<DateOnly>("Date"), checkpointInterval: 1024);
```

`IndexKey<DateOnly>("Date")` uses the reader's registered `DateOnly` runtime parser if available. Otherwise it uses `yyyy-MM-dd` with invariant culture. Both use the default `DateOnly` comparer. An explicit `format` or `culture` on the index key takes precedence over the runtime parser. A smaller checkpoint interval records more checkpoints and reduces the number of rows between the starting checkpoint and the target.

## Build the sidecar

Run this as a preparation step after writing the source file.

```csharp
csv.BuildIndexes("orders.csv", dateIndex);
```

`BuildIndexes` scans the whole file and writes `orders.csv.sparse.idx` beside it. Each call rebuilds the sidecar, even when the existing sidecar is current. Call it after updating the source or when you want to change the key or checkpoint interval.

## Look up a key

Use the same reader settings, parser registration, and index key settings to load the existing sidecar. This can run in a separate process from the build step.

```csharp
var date = new DateOnly(2026, 5, 17);
using var rows = csv.WithIndexedLookup("orders.csv", dateIndex, date);
while (rows.Read())
    Console.WriteLine(rows["OrderId"]);
```

The example prints order IDs `17` and `18`.

The lookup returns all matching rows in source order. By default, it requires a valid sidecar. Use `dateIndex.WithOptionalIndex()` to allow a scan when the sidecar is missing or the reader configuration cannot seek. `WithRequiredIndex()` restores strict lookup. A stale, corrupt, unreadable, or incompatible sidecar for a seekable source fails under either policy.

For partitioned CSV, call `selected.BuildIndexes(dateIndex)` during preparation. At lookup time, call `selected.WithIndexedLookup(dateIndex, date).ToDataReader()` on the selected partition query. With headers enabled, each indexed partition must carry its own header. Sparse indexing is unsupported for partitioned CSV with `HeaderRepeat = false`. [Read partitioned CSV files](../file-partitions/) · [Shared-header support](https://github.com/Seddryck/PocketCsvReader/issues/441)

An already-open `IDataReader` can use `WithIndexedLookup(dateIndex.WithOptionalIndex(), date)`. It scans the reader, skips missing or null keys, and owns the input reader until disposal. The scan works on unsorted records.

## Use the reader's runtime parser

If the source stores dates as `dd/MM/yyyy`, register the parser on the reader and keep the same index definition. Use this reader in both the build and lookup steps above.

```csharp
using System.Globalization;
using PocketCsvReader;
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

ParseSpan<DateOnly> parseDate = ParseOrderDate;
var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithParsers(p => p.WithParser(parseDate))
    .Build();
var dateIndex = new SparseIndex(new IndexKey<DateOnly>("Date"), checkpointInterval: 1024);

static DateOnly ParseOrderDate(ReadOnlySpan<char> text)
    => DateOnly.ParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture);
```

PocketCsvReader uses this parser for the source keys and the checkpoint text. It derives the compatibility ID internally and checks it before parsing any checkpoints. Rebuild the sidecar if the parser or its compiled module changes.

Persisted indexes require a deterministic static method or non-capturing lambda. Avoid dependencies on mutable settings, ambient culture, or the clock because PocketCsvReader cannot detect changes in that state. A capturing lambda or instance method can still be used for a file scan when the index is optional and no sidecar is present. [Register custom parsers](../fluent-api-runtime-parsers/)

## Detect a stale sidecar

The sidecar stores the source file's byte length and UTC last-write time. During a build, PocketCsvReader samples both values before and after the scan and checks again before replacing the sidecar. If either changes, the build fails before publishing the replacement.

At lookup, a mismatch in either stored value causes PocketCsvReader to reject the sidecar as stale before using its checkpoints. It also validates the sidecar checksum and checks that the indexed column, key type, parser and comparer settings, and reader configuration are compatible. A source edit that preserves both the byte length and timestamp can go undetected.

## Supported readers

All sparse index builders require records sorted in ascending order under the configured key comparer, with a present, parseable key on every data record. The source must be a seekable, uncompressed UTF-8 file. A UTF-8 BOM is supported.

| Reader | Configuration for indexed seeking |
| --- | --- |
| CSV | ASCII CSV syntax. The reader loads the configured header rows before seeking. |
| NDJSON | One record per root, with dynamic or projected fields. |
| Fixed-width | With or without a header. The reader loads the header before seeking. |
| Logfmt and LTSV | Dynamic or projected fields. |
| Common and Combined web logs | The reader uses the format's fixed field layout. |
| W3C Extended web logs | The reader loads the leading `#Fields` layout before seeking. The build rejects later directives that change it. |
| CEF and LEEF | Explicit attribute columns. |
