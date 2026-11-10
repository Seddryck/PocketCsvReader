---
title: Choose an indexing strategy
tags: [usage]
---

Start with the part of the search you want to avoid. Partition metadata can rule out entire CSV files based on their directory, filename, or assigned values. An index can rule out more candidates or locate matching records inside a file. You can combine a partition filter with a sparse lookup so only selected files are searched. [Read partitioned CSV files](../file-partitions/)

## Available index

Add `PocketCsvReader.Indexing` to use sparse indexes for exact-key lookups in supported sorted files. For example, you can select a month using partition metadata and seek to a particular date within each selected file. The sparse index guide covers supported readers, setup, building sidecars, and querying them. [Use sparse indexes](../sparse-indexes/)

## Proposed index types

These issues describe planned strategies. Their APIs and file-format support are tracked in the proposals.

| Lookup need | Proposed strategy | Issue |
| --- | --- | --- |
| Skip files that cannot contain an exact key | A Bloom filter rules out definite misses. Files that might match still need an exact check. | [#430](https://github.com/Seddryck/PocketCsvReader/issues/430) |
| Skip blocks that cannot match a range or exact value | A zone map stores a minimum and maximum per block. Candidate blocks still need an exact check. | [#431](https://github.com/Seddryck/PocketCsvReader/issues/431) |
| Find an exact key in an unsorted file | A hash index maps each key to its matching record locations. | [#432](https://github.com/Seddryck/PocketCsvReader/issues/432) |
| Combine equality and set-membership filters over categorical fields | Bitmap indexes combine sets of matching row ordinals. | [#433](https://github.com/Seddryck/PocketCsvReader/issues/433) |
| Find records containing specific tokens or elements | An inverted index maps each term to the records that contain it. | [#434](https://github.com/Seddryck/PocketCsvReader/issues/434) |
