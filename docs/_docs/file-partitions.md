---
title: Read partitioned CSV files
tags: [usage]
---

Use a partition query when file paths or metadata tell you which rows an entire file contains. Partition metadata describes every row in a file and stays separate from the CSV columns.

## Attach typed metadata

The partition keys can also be ordinary CSV columns. In this example, each file contains `Country` and `Year` columns, and its descriptor supplies the same values as file-level metadata.

<!-- example: typed-file-partitions -->
```csharp
using System.Text;
using PocketCsvReader;
using PocketCsvReader.Configuration;

var files = new[]
{
    FileSource.Partitioned("orders-001.csv", new OrderPartition("BE", 2025),
        () => new MemoryStream(Encoding.UTF8.GetBytes("OrderId,Country,Year\n41,BE,2025\n"))),
    FileSource.Partitioned("orders-002.csv", new OrderPartition("BE", 2026),
        () => new MemoryStream(Encoding.UTF8.GetBytes("OrderId,Country,Year\n42,BE,2026\n"))),
    FileSource.Partitioned("orders-003.csv", new OrderPartition("FR", 2026),
        () => new MemoryStream(Encoding.UTF8.GetBytes("OrderId,Country,Year\n43,FR,2026\n")))
};
var query = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(files)
    .Build();
var selected = query.WithPartitionFilter(p => p.Country == "BE" && p.Year == 2026);

foreach (var file in selected.EnumerateFiles())
    Console.WriteLine($"{file.Path}: {file.Partition.Country}, {file.Partition.Year}");

using var reader = selected.ToDataReader();
while (reader.Read())
    Console.WriteLine($"{reader["OrderId"]},{reader["Country"]},{reader["Year"]}");

public sealed record OrderPartition(string Country, int Year);
```

<!-- output: typed-file-partitions -->
```text
orders-002.csv: BE, 2026
42,BE,2026
```

`WithPartitionFilter` returns a filtered query. Calling `EnumerateFiles()` on the original query lists all discovered files. Calling `selected.EnumerateFiles()` lists only the files that pass the filter. Here, `orders-001.csv` fails the year condition and `orders-003.csv` fails the country condition. Both stay unopened. The first output line comes from the selected descriptor. The second comes from the CSV columns in the selected file.

Omit the opener to read the file at the supplied path. To look up metadata from another source, call `FromFiles(paths).WithPartitions(p => p.FromPath(path => partitionsByPath[path]))`. The selector receives each path as supplied and runs during discovery. Use `FromFiles(paths, open)` to supply a deferred opener for the paths.

## Discovery and stream ownership

Building a query, enumerating its descriptors, and constructing a reader open zero input streams. Each enumeration and reader execution discovers the source independently, in source order. Use a replayable sequence and open a fresh stream on each call if you plan to discover or read more than once.

The first row request opens the first needed file. This includes the database adapter's `HasRows` property, which can buffer the first row. Use `Read` or `ReadAsync` to advance the reader. Deferred custom openers run synchronously with either method. Empty input yields an empty reader. Invalid descriptor entries surface when reached, and file access errors surface on the read that reaches the file.

The batch owns each stream returned by an opener. It closes the previous stream before opening the next, closes the active stream on disposal or read failure, and leaves unvisited inputs unopened. Configure CSV dialect, schema, and [row filters](../reader-filtering/) on the reader builder before selecting the files.

## Filter files before opening them

A typed `CsvFileQuery<TPartition>` supports a delegate such as `p => p.Country == "BE" && p.Year == 2026`. Call `WithPartitionFilter` after `Build()` on the file query. The delegate receives the complete partition metadata for each file.

For `CsvFileQuery<NamedPartitions>`, the named overload accepts one or more `PartitionCondition.Equal(key, value)` and `PartitionCondition.NotEqual(key, value)` conditions. Specify only the keys you want to constrain. Keys and values use ordinal, case-sensitive comparison.

The query applies conditions with short-circuiting AND, in array order and then in the order filters were registered. An unknown key on a complete descriptor raises `KeyNotFoundException` when reached, including for `NotEqual`. Delegate exceptions propagate. `EnumerateFiles()` lists the same selection used by reader execution. Files that fail the filters remain unopened, and an entirely rejected input yields an empty reader.

CSV row filters still run on rows in the selected files. When headers are enabled, a filtered query requires `HeaderRepeat = true` so each accepted file supplies its own header.

## Extract partitions from filenames

Given paths such as `orders-BE-2026.csv`, capture both `country` and `year` and filter on `year` alone. Both 2026 files pass, regardless of country.

<!-- example: filename-partitions -->
```csharp
using System.Text;
using PocketCsvReader;
using PocketCsvReader.Configuration;

var csvByPath = new Dictionary<string, string>
{
    ["orders-BE-2025.csv"] = "OrderId,Country,Year\n41,BE,2025\n",
    ["orders-BE-2026.csv"] = "OrderId,Country,Year\n42,BE,2026\n",
    ["orders-FR-2026.csv"] = "OrderId,Country,Year\n43,FR,2026\n"
};
var paths = csvByPath.Keys.Order(StringComparer.Ordinal);
var selected = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(paths, path => new MemoryStream(Encoding.UTF8.GetBytes(csvByPath[path])))
    .WithPartitions(p => p.FromFileName("orders-{country}-{year}.csv"))
    .Build()
    .WithPartitionFilter(PartitionCondition.Equal("year", "2026"));

foreach (var file in selected.EnumerateFiles())
    Console.WriteLine(file.Path);

using var reader = selected.ToDataReader();
while (reader.Read())
    Console.WriteLine($"{reader["OrderId"]},{reader["Country"]},{reader["Year"]}");
```

<!-- output: filename-partitions -->
```text
orders-BE-2026.csv
orders-FR-2026.csv
42,BE,2026
43,FR,2026
```

The filename supplies the value used for selection. The reader then returns the `Country` and `Year` columns from each accepted CSV file.

The pattern matches the entire basename using ordinal, case-sensitive comparison. A placeholder is `{key}`. {% raw %}Write `{{` and `}}` for literal braces.{% endraw %} Each capture contains at least one character. The first occurrence of the next literal separates adjacent captures, and the final literal is matched at the end of the name. For example, `orders-BE-2026-extra.csv` gives `country = BE` and `year = 2026-extra` with the pattern above.

A pattern must have at least one placeholder. Adjacent placeholders without a literal separator, repeated keys, empty keys, whitespace in keys, unmatched braces, and path separators are rejected when configuring the query. A filename that fails to match raises `FormatException` during discovery.
