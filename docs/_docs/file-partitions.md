---
title: Read partitioned CSV files
tags: [usage]
---

Use a partition query when file paths or metadata tell you which rows an entire file contains. Partition metadata describes every row in a file and stays separate from the CSV columns.

## Attach typed metadata

<!-- example: typed-file-partitions -->
```csharp
using System.Text;
using PocketCsvReader;
using PocketCsvReader.Configuration;

var files = new[]
{
    FileSource.Partitioned("orders-001.csv", new OrderPartition("BE", 2026),
        () => new MemoryStream(Encoding.UTF8.GetBytes("OrderId\n42\n")))
};
var query = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(files)
    .Build();

foreach (var file in query.EnumerateFiles())
    Console.WriteLine($"{file.Path}: {file.Partition.Country}, {file.Partition.Year}");

using var reader = query.ToDataReader();
while (reader.Read())
    Console.WriteLine(reader["OrderId"]);

public sealed record OrderPartition(string Country, int Year);
```

<!-- output: typed-file-partitions -->
```text
orders-001.csv: BE, 2026
42
```

Omit the opener to read the file at the supplied path. To look up metadata from another source, call `FromFiles(paths).WithPartitions(p => p.FromPath(path => partitionsByPath[path]))`. The selector receives each path as supplied and runs during discovery. Use `FromFiles(paths, open)` to supply a deferred opener for the paths.

## Discovery and stream ownership

Building a query, enumerating its descriptors, and constructing a reader open zero input streams. Each enumeration and reader execution discovers the source independently, in source order. Use a replayable sequence and open a fresh stream on each call if you plan to discover or read more than once.

The first row request opens the first needed file. This includes the database adapter's `HasRows` property, which can buffer the first row. Use `Read` or `ReadAsync` to advance the reader. Deferred custom openers run synchronously with either method. Empty input yields an empty reader. Invalid descriptor entries surface when reached, and file access errors surface on the read that reaches the file.

The batch owns each stream returned by an opener. It closes the previous stream before opening the next, closes the active stream on disposal or read failure, and leaves unvisited inputs unopened. Configure CSV dialect, schema, and [row filters](../reader-filtering/) on the reader builder before selecting the files.
