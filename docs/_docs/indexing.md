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
