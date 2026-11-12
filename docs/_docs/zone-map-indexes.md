---
title: Use zone maps
tags: [usage]
---

A zone map records the typed minimum and maximum for each contiguous block of CSV data records. For an equality or range lookup, the reader seeks past blocks whose bounds rule out a match. It evaluates the exact predicate on records in blocks whose bounds overlap the query or are unavailable. Unsorted files are supported, and lookups are most selective when nearby records have similar key values.

## Build and query a zone map

Add `PocketCsvReader.Indexing`. Define a key by header name or zero-based source ordinal, and choose a positive number of data records per block.

```csharp
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

var orders = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(new[] { "orders-2026-05.csv", "orders-2026-06.csv" })
    .WithPartitions(p => p.FromFileName("orders-{month}.csv"))
    .Build();

var amounts = new ZoneMapIndex(new IndexKey<decimal>("Amount"), blockSize: 1024);
orders.BuildIndexes(amounts);

var range = new IndexRange<decimal>(100m, 200m, includeLower: true, includeUpper: false);
using var rows = orders.WithIndexedLookup(amounts, range).ToDataReader();
while (rows.Read())
    Console.WriteLine(rows["Amount"]);
```

Both endpoints are inclusive by default. Pass a single typed value, such as `orders.WithIndexedLookup(amounts, 100m)`, for equality. For a plain `CsvReader`, use `csv.BuildIndexes(path, amounts)` and `csv.WithIndexedLookup(path, amounts, range)`.

`BuildIndexes` scans every data record in each selected file, regardless of row filters, skip, or take. It writes or replaces a versioned `.zonemap.idx` sidecar beside each file if the source byte length and UTC modification time remain unchanged. Partition filters limit which files are considered by builds and run before sidecar checks during lookup. Lookup preserves source order, skips missing and null keys, and applies row filters and pagination to the matches.

## Key and sidecar rules

Zone maps share the [Sparse key rules](../sparse-indexes/), including registered runtime parsers, explicit formats and cultures, and string comparison. Persisted runtime parsers must be deterministic static methods or non-capturing lambdas. Use compatible key, dialect, reader, and block size settings for builds and lookups. Lookup validates the sidecar version, checksum, source stamps, and compatibility.

By default every selected file needs a usable sidecar. `WithOptionalIndex()` allows a sequential scan when a sidecar is missing or the source configuration cannot use one. A stale, unreadable, corrupt, or incompatible sidecar for a supported source fails under either policy. Lookup never creates or rebuilds a sidecar. `WithRequiredIndex()` restores strict lookup.

Zone maps support local, seekable, uncompressed UTF-8 files with ASCII CSV syntax and an optional BOM. When headers are enabled, each file needs its own header. Numbered comment rows, array fields, leading row counts, custom stream openers, and bad-data policies require optional scans. An already-open `IDataReader` also requires an optional index. The returned reader uses the definition's key settings and owns the input.
