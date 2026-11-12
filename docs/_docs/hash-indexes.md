---
title: Use hash indexes
tags: [usage]
---

A hash index stores the stable key hash and parser-confirmed byte location of each CSV data record with a non-null key. An equality lookup seeks to records with the requested hash and checks exact typed equality to resolve collisions. Lookups preserve duplicates and source order, including when the file is sorted by another column.

## Build and query a hash index

Add `PocketCsvReader.Indexing`. Define the key by header name or zero-based source ordinal.

```csharp
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

var orders = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(new[] { "orders-2026-05.csv", "orders-2026-06.csv" })
    .WithPartitions(p => p.FromFileName("orders-{month}.csv"))
    .Build();

var customer = new HashIndex(
    new IndexKey<string>("CustomerId", comparison: StringComparison.OrdinalIgnoreCase));
orders.BuildIndexes(customer);

using var rows = orders.WithIndexedLookup(customer, "C12345").ToDataReader();
while (rows.Read())
    Console.WriteLine(rows["CustomerId"]);
```

For a plain `CsvReader`, use `csv.BuildIndexes(path, customer)` and `csv.WithIndexedLookup(path, customer, "C12345")`.

`BuildIndexes` scans every data record in each selected file, regardless of row filters, skip, or take. It replaces the versioned `.hash.idx` sidecar only if the source byte length and UTC modification time stay unchanged. Partition filters limit the selected files before lookup consults sidecars. Missing and null keys never match. Row filters and pagination apply after exact equality.

## Key and sidecar rules

Hash indexes share the [Sparse key rules](../sparse-indexes/), including registered runtime parsers, explicit formats and cultures, and string comparison. Persisted runtime parsers must be deterministic static methods or non-capturing lambdas. Lookup validates the sidecar version, checksum, source stamps, and compatibility with the key, hash, and reader configuration.

The default requires a usable sidecar for each selected file. `WithOptionalIndex()` allows a sequential scan when a sidecar is missing or the reader configuration cannot use one. A stale, unreadable, corrupt, or incompatible sidecar for a supported source fails under either policy. Lookup never creates or rebuilds a sidecar. `WithRequiredIndex()` restores strict lookup.

Hash indexes support local, seekable, uncompressed UTF-8 files with ASCII CSV syntax and an optional BOM. When headers are enabled, each file needs its own header. Numbered comment rows, array fields, leading row counts, custom stream openers, and bad-data policies require optional scans. An already-open `IDataReader` also requires an optional index. The returned reader uses the definition's key settings and owns the input.

The current sidecar uses 24 bytes per indexed record plus a fixed header. Lookup reads the sidecar to verify its checksum, then finds matching hash entries by binary search. Selective lookups can skip parsing most of the CSV. The build cost and sidecar size grow with the number of indexed records.
