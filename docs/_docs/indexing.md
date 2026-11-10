---
title: Choose an indexing strategy
tags: [usage]
---

Start with the part of the search you want to avoid. Partition metadata can rule out entire CSV files based on their directory, filename, or assigned values. An index can rule out more candidates or locate matching records inside a file. You can combine a partition filter with a sparse lookup so only selected files are searched. [Read partitioned CSV files](../file-partitions/)

## Available index

Add `PocketCsvReader.Indexing` to use a sparse index. [Use sparse indexes](../sparse-indexes/)

| Index | Use case and fit | Constraints |
| --- | --- | --- |
| Sparse index | Repeated exact-key lookups on a sorted field. For example, select a month using partition metadata, then seek to a particular date in each selected file. | Requires a supported, seekable, uncompressed UTF-8 file. Every data record needs a parseable key, and records must be in ascending order under the configured comparer. |

## Proposed index types

These proposals target CSV. They describe when each strategy fits and what limits its effectiveness.

| Index | Use case and fit | Constraints | Proposal |
| --- | --- | --- | --- |
| Bloom filter | Skip entire files before opening them. Fits exact-key searches when many files lack the key, such as finding a customer across monthly partitions. | A possible match still requires reading the file and checking exact equality. Hashing must be stable and consistent with the key equality rules. | [#430](https://github.com/Seddryck/PocketCsvReader/issues/430) |
| Zone map | Skip blocks for range or equality filters. Fits files where nearby rows have similar values, even if the file is unsorted. | Stores minimum and maximum values per block. Overlapping or unavailable bounds leave a block to scan. The proposal starts with seekable, uncompressed UTF-8 CSV. | [#431](https://github.com/Seddryck/PocketCsvReader/issues/431) |
| Hash index | Find matching records by exact key inside an unsorted file, such as orders for one customer in a file sorted by date. | Stores a location for every indexed record, including duplicates. Persisted hashes must be stable, and collisions are resolved by checking exact equality. The proposal starts with seekable, uncompressed UTF-8 CSV. | [#432](https://github.com/Seddryck/PocketCsvReader/issues/432) |
| Bitmap index | Combine equality and set-membership filters, such as status in a set of values and region equal to `EU`. Fits columns with few distinct values. | Uses one bitmap per distinct value. Combining fields requires a common data-row numbering scheme within each file. | [#433](https://github.com/Seddryck/PocketCsvReader/issues/433) |
| Inverted index | Find records containing any or all requested tokens or elements, such as both `urgent` and `refund` tags. | Requires a term extractor and consistent normalization and equality rules. Storage grows with the number of distinct term-to-record entries. | [#434](https://github.com/Seddryck/PocketCsvReader/issues/434) |
