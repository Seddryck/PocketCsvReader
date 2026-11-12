---
title: Use Bloom indexes
tags: [usage]
---

A Bloom index helps with equality lookups on an unsorted CSV column. For example, monthly files might be sorted by date, while readers often look for one customer ID. After partition filtering, the Bloom sidecar can rule out a file without opening its CSV stream. If the sidecar reports a possible match, the reader scans the file and applies exact typed equality.

## Build and query an index

Add `PocketCsvReader.Indexing`. Select the source files and define the key by header name or zero-based source ordinal:

```csharp
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

var orders = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(new[] { "orders-2026-05.csv", "orders-2026-06.csv" })
    .WithPartitions(p => p.FromFileName("orders-{month}.csv"))
    .Build();

var customer = new BloomIndex(
    new IndexKey<string>("CustomerId", comparison: StringComparison.OrdinalIgnoreCase),
    expectedDistinctCount: 20_000,
    falsePositiveProbability: 0.01,
    maxBytes: 1024 * 1024);

orders.BuildIndexes(customer);

using var rows = orders.WithIndexedLookup(customer, "C12345").ToDataReader();
while (rows.Read())
    Console.WriteLine(rows["CustomerId"]);
```

Run `BuildIndexes` as a preparation step. It scans all data rows in each selected file, regardless of row filters, skip, or take. It writes or replaces a `.bloom.idx` sidecar beside each file only if the CSV byte length and UTC modification time are unchanged. Partition predicates limit which files the build or lookup considers. For a plain `CsvReader`, use `csv.BuildIndexes(path, customer)` and `csv.WithIndexedLookup(path, customer, "C12345")`.

The expected distinct count is per file. The probability is a sizing target; a smaller cap or more distinct values can make false positives more common. The bitset is a multiple of 32 bytes, with a default cap of 1 MiB and a supported cap range from 32 bytes to 128 MiB. The sizing options apply when building; a lookup uses the size recorded in the sidecar.

## Key and sidecar rules

Bloom and [Sparse indexes](../sparse-indexes/) share `IndexKey<TKey>` parsing and comparison rules. By default, the reader's registered runtime parser takes precedence when no format or culture is set. Persisted runtime parsers must be deterministic static methods or non-capturing lambdas. Missing and null keys do not match. Lookup preserves source order, composes with row filters, and applies skip and take to the resulting rows.

Use the same dialect, reader and key settings in the build and lookup processes. The sidecar contains a versioned split-block bitset and validates the source stamps, checksum, and compatibility of the reader, parser, comparison and stable hash. Culture-sensitive string keys use collation sort keys, so a change in the applicable sort data can require a rebuild.

The default requires a valid sidecar. `WithOptionalIndex()` allows a sequential equality scan when a sidecar is missing or the source configuration cannot use one. If a supported source has a stale, unreadable, corrupt or incompatible sidecar, lookup fails under either policy. Lookup never builds or updates sidecars. Use `WithRequiredIndex()` to restore strict lookup.

Bloom sidecars are supported for local filesystem CSV paths with uncompressed UTF-8 settings, ASCII CSV syntax and a header in each file when headers are enabled. Numbered comment rows, array fields, leading row counts, custom stream openers and bad-data policies require optional scans. An already-open `IDataReader` also requires an optional index. The lookup reader uses the definition's key settings and owns the input reader.
