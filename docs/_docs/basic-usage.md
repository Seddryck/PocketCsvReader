---
title: Read your first CSV
tags: [quick-start]
---

<span id="1-accessing-data-with-idatareader"></span>
<span id="2-reading-into-a-datatable"></span>
<span id="3-reading-as-arrays"></span>
<span id="4-mapping-records-to-strongly-typed-objects"></span>
<span id="5-reading-as-dynamic-objects"></span>
<span id="asynchronous-strongly-typed-streaming"></span>
<span id="best-practices-and-additional-notes"></span>
<span id="error-handling"></span>
<span id="example-mapping-to-a-person-class"></span>
<span id="features"></span>
<span id="from-a-file-to-a-datatable"></span>
<span id="from-a-file-to-a-idatareader"></span>
<span id="from-a-file-to-an-array"></span>
<span id="from-a-stream-to-a-datatable"></span>
<span id="from-a-stream-to-a-idatareader"></span>
<span id="from-a-stream-to-an-array"></span>
<span id="from-a-stream-to-strongly-typed-objects"></span>
<span id="getting-started"></span>
<span id="initialization"></span>
<span id="reading-csv-data"></span>

Install [PocketCsvReader](../installation/). This complete console example reads a small customer export without requiring a file on disk.

## Input

```csv
Id,Name
1,Ada
2,Grace
```

## Read rows

<!-- example: first-csv -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .Build();

using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n2,Grace\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
{
    var id = reader.GetInt32(reader.GetOrdinal("Id"));
    var name = reader.GetString(reader.GetOrdinal("Name"));
    Console.WriteLine($"{id}: {name}");
}
```

<!-- output: first-csv -->
```text
1: Ada
2: Grace
```

`WithHeader()` uses the first row for column names. For headerless data use `WithoutHeader()` and access columns by position. The line terminator is explicit here because the input uses LF. See [CSV and TSV](../csv-dialect-descriptor/) for quoting, comments, presets, and dialect defaults.

To read a file, replace `csv.ToDataReader(stream)` with `csv.ToDataReader("customers.csv")`. The data reader owns its supplied stream and closes it on disposal. A filename overload streams input too; choosing a filename does not itself load the whole file.

## Map to objects

The default CSV object mapper requires a public constructor whose parameter count and order match the data columns, such as `public record Customer(int Id, string Name)`. A property-only class is insufficient. Use `csv.To<Customer>("customers.csv")` and enumerate it while its input is available.

Column names do not bind constructor parameters. [Map rows to objects](../mapper-object-builder/) contains a complete order import, custom parsers, and a selected-column mapper.

## Next steps

- [Choose an output model](../output-models/) for arrays, dictionaries, dynamic rows, tables, or typed objects.
- [Configure schemas and cultures](../fluent-api-schema/) when values need explicit types or formats.
- [Read asynchronously](../async-reading/) and understand [stream lifetimes](../stream-lifetimes/).
- [Handle optional fields and malformed input](../validation-and-errors/).
