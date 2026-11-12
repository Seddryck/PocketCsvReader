---
title: Choose an indexing strategy
tags: [usage]
---

Start with the part of the search you want to avoid. Partition metadata can rule out entire CSV files based on their directory, filename, or assigned values. An index can rule out more candidates or locate matching records inside a file. You can combine a partition filter with an indexed lookup so only selected files are searched. [Read partitioned CSV files](../file-partitions/)

## Available indexes

Add `PocketCsvReader.Indexing` to use [sparse indexes](../sparse-indexes/), [Bloom indexes](../bloom-indexes/), [zone maps](../zone-map-indexes/), [hash indexes](../hash-indexes/), [bitmap indexes](../bitmap-indexes/), and [inverted indexes](../inverted-indexes/).

| Index | Use case and fit | Constraints |
| --- | --- | --- |
| Sparse index | Repeated exact-key lookups on a sorted field. For example, select a month using partition metadata, then seek to a particular date in each selected file. | Requires a supported, seekable, uncompressed UTF-8 file. Every data record needs a parseable key, and records must be in ascending order under the configured comparer. |
| Bloom index | Exact-key lookups on unsorted CSV fields when many files lack the key. For example, look up a customer across monthly partitions. | A definite negative skips the CSV stream. A possible match requires scanning that file and checking exact equality. |
| Zone map | Range and exact-key lookups on CSV columns whose values tend to cluster within blocks, including unsorted files. | Stores typed minimum and maximum values per block. Blocks with overlapping or unavailable bounds are scanned. Requires seekable, uncompressed UTF-8 CSV. |
| Hash index | Direct exact-key lookups on unsorted CSV columns, such as customer IDs in a file sorted by date. | Stores a hash and record location per non-null key, including duplicates. Lookup reads candidate records and checks exact equality. Requires seekable, uncompressed UTF-8 CSV. |
| Bitmap index | Typed equality and set membership across low-cardinality CSV columns, such as status and region. | Stores a bitmap per distinct value and combines them before reading CSV records. Requires seekable, uncompressed UTF-8 CSV. |
| Inverted index | Finds records containing any or all requested terms extracted from a typed CSV field, such as both `urgent` and `refund` tags. | Stores a posting per distinct term in each record. Requires a deterministic extractor and seekable, uncompressed UTF-8 CSV. |

## Chain indexed filters

Chain `WithIndexedLookup` calls on a `CsvFileQuery<TPartition>` to require every indexed predicate. Build the sidecars separately before querying. For example, if each orders file is sorted by date, combine a customer Bloom index with a sparse date index.

```csharp
using PocketCsvReader.Indexing;

var customer = new BloomIndex(new IndexKey<string>("CustomerId", comparison: StringComparison.OrdinalIgnoreCase));
var date = new SparseIndex(new IndexKey<DateOnly>("Date"), checkpointInterval: 1024);
orders.BuildIndexes(customer);
orders.BuildIndexes(date);

using var rows = orders
    .WithIndexedLookup(date, new DateOnly(2026, 5, 17))
    .WithIndexedLookup(customer, "C12345")
    .ToDataReader();
```

Partition filters run first, then Bloom file pruning, zone-map block pruning, bitmap intersection, inverted posting intersection, hash candidate selection, and sparse seeking. Each index narrows the candidates from the preceding indexes. A file with zero candidates skips the remaining sidecars. Lookup call order does not change the order of index types.

On surviving rows, sparse equality runs first so an ordered scan can stop at the end of the target range. Then the reader checks zone-map predicates, bitmap and inverted predicates that need a fallback, hash equality, and Bloom equality. Existing row filters run in their configured order, followed by skip and take. Result order and source row numbers are preserved across skipped files and records.

`WithOptionalIndex()` applies to one definition. A missing or unsupported optional index leaves the current candidates eligible and applies its exact predicate to them. Stale, unreadable, corrupt, and incompatible sidecars fail when consulted.
