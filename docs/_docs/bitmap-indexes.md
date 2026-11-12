---
title: Use bitmap indexes
tags: [usage]
---

A bitmap index records the data-row ordinals for each distinct typed value in one or more CSV columns. A lookup unions the bitmaps for alternative values and intersects the bitmaps for conditions joined by `And`. The reader seeks to the resulting records in source order and reads each selected record once. Columns with few distinct values, such as order status and region, are a good fit.

## Build and query a bitmap index

Add `PocketCsvReader.Indexing`. Define each key by header name or zero-based source ordinal.

```csharp
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

var orders = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(new[] { "orders-2026-05.csv", "orders-2026-06.csv" })
    .WithPartitions(p => p.FromFileName("orders-{month}.csv"))
    .Build();

var categories = new BitmapIndex(
    new IndexKey<string>("Status", comparison: StringComparison.OrdinalIgnoreCase),
    new IndexKey<string>("Region"));
orders.BuildIndexes(categories);

var predicate = BitmapPredicate.In("Status", "shipped", "delivered")
    .And(BitmapPredicate.Equal("Region", "EU"));
using var rows = orders.WithIndexedLookup(categories, predicate).ToDataReader();
while (rows.Read())
    Console.WriteLine(rows["Status"]);
```

Pass the same name or ordinal used in the key definition to `Equal` or `In`. Each supplied value must have the key's type. An empty membership set matches zero rows. For a plain `CsvReader`, use `csv.BuildIndexes(path, categories)` and `csv.WithIndexedLookup(path, categories, predicate)`.

`BuildIndexes` scans every data record in each selected file, regardless of row filters, skip, or take. It replaces the versioned `.bitmap.idx` sidecar only if the source byte length and UTC modification time stay unchanged. Partition filters run before the reader consults sidecars. Missing and null keys never match. Row filters and pagination apply to the selected rows.

## Key and sidecar rules

Bitmap indexes share the [Sparse key rules](../sparse-indexes/), including registered runtime parsers, explicit formats and cultures, and string comparison. Persisted runtime parsers must be deterministic static methods or non-capturing lambdas. Lookup validates the sidecar version, checksum, source stamps, and compatibility with the ordered key definitions and reader settings.

By default each selected file requires a usable sidecar. `WithOptionalIndex()` allows a sequential scan when a sidecar is missing or the reader configuration cannot use one. A stale, unreadable, corrupt, or incompatible sidecar for a supported source fails under either policy. Lookup never creates or rebuilds a sidecar. `WithRequiredIndex()` restores strict lookup.

Bitmap indexes support local, seekable, uncompressed UTF-8 CSV with ASCII syntax and an optional BOM. When headers are enabled, each file needs its own header. Numbered comment rows, array fields, leading row counts, custom stream openers, and bad-data policies require optional scans. An already-open `IDataReader` also requires an optional index. The returned reader uses the definition's key settings and owns the input.

The sidecar stores a record location for every data row and only the nonzero 64-bit words of each value's bitmap. Lookup reads the sidecar to verify its checksum before combining the bitmaps. An empty intersection requires no CSV data records. Adjacent matches are read in one run. A broad filter with many scattered matches can spend more time seeking than a sequential scan.
