---
title: Choose an indexing strategy
tags: [usage]
---

Start with the part of the search you want to avoid. Partition metadata can rule out entire CSV files based on their directory, filename, or assigned values. An index can rule out more candidates or locate matching records inside a file. You can combine a partition filter with an indexed lookup so only selected files are searched. [Read partitioned CSV files](../file-partitions/)

## Available indexes

Add `PocketCsvReader.Indexing` to use [sparse indexes](../sparse-indexes/), [Bloom indexes](../bloom-indexes/), and [zone maps](../zone-map-indexes/).

| Index | Use case and fit | Constraints |
| --- | --- | --- |
| Sparse index | Repeated exact-key lookups on a sorted field. For example, select a month using partition metadata, then seek to a particular date in each selected file. | Requires a supported, seekable, uncompressed UTF-8 file. Every data record needs a parseable key, and records must be in ascending order under the configured comparer. |
| Bloom index | Exact-key lookups on unsorted CSV fields when many files lack the key. For example, look up a customer across monthly partitions. | A definite negative skips the CSV stream. A possible match requires scanning that file and checking exact equality. |
| Zone map | Range and exact-key lookups on CSV columns whose values tend to cluster within blocks, including unsorted files. | Stores typed minimum and maximum values per block. Blocks with overlapping or unavailable bounds are scanned. Requires seekable, uncompressed UTF-8 CSV. |

## Proposed index types

These proposals target CSV. They describe when each strategy fits and what limits its effectiveness.

| Index | Use case and fit | Constraints | Proposal |
| --- | --- | --- | --- |
| Hash index | Find matching records by exact key inside an unsorted file, such as orders for one customer in a file sorted by date. | Stores a location for every indexed record, including duplicates. Persisted hashes must be stable, and collisions are resolved by checking exact equality. The proposal starts with seekable, uncompressed UTF-8 CSV. | [#432](https://github.com/Seddryck/PocketCsvReader/issues/432) |
| Bitmap index | Combine equality and set-membership filters, such as status in a set of values and region equal to `EU`. Fits columns with few distinct values. | Uses one bitmap per distinct value. Combining fields requires a common data-row numbering scheme within each file. | [#433](https://github.com/Seddryck/PocketCsvReader/issues/433) |
| Inverted index | Find records containing any or all requested tokens or elements, such as both `urgent` and `refund` tags. | Requires a term extractor and consistent normalization and equality rules. Storage grows with the number of distinct term-to-record entries. | [#434](https://github.com/Seddryck/PocketCsvReader/issues/434) |
