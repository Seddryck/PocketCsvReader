# PocketCsvReader

PocketCsvReader reads CSV and other structured text through forward-only data readers and streaming row outputs. Optional packages support JSON/JSONC, NDJSON, fixed-width, LTSV, logfmt, web logs, and security logs. The projects target .NET 8, 9, and 10.

[Documentation](https://seddryck.github.io/PocketCsvReader/docs/why-pocketcsvreader/) · [Installation](https://seddryck.github.io/PocketCsvReader/docs/installation/) · [Contributing](contributing.md)

## Packages

| Package | Purpose |
| --- | --- |
| `PocketCsvReader` | CSV/TSV and shared APIs |
| `PocketCsvReader.Json` | JSON and JSONC with projection and stable field layout for object records |
| `PocketCsvReader.Ndjson` | NDJSON with projection and stable field layout for object records |
| `PocketCsvReader.FixedWidth` | Fixed character positions |
| `PocketCsvReader.KeyValue` | LTSV and logfmt with column projection, aliases, and stable field layout |
| `PocketCsvReader.WebLogs` | Common, Combined, W3C |
| `PocketCsvReader.SecurityLogs` | CEF and LEEF |
| `PocketCsvReader.Arrow` | Apache Arrow output |
| `PocketCsvReader.Arrow.ColumnarJson` | Apache Arrow Integration JSON, ClickHouse JSONColumns and JSONColumnsWithMetadata |
| `PocketCsvReader.Chrononuensis` | Temporal period parsers |

## Columnar JSON

Install `PocketCsvReader.Arrow.ColumnarJson` and import `PocketCsvReader.Arrow` and
`PocketCsvReader.Arrow.ColumnarJson`. Use `new ArrowIntegrationJsonReader()` for
Arrow fixtures, `new ClickHouseJsonColumnsReader(schema)` for JSONColumns, or
`ClickHouseJsonColumnsReader.WithMetadata()` for JSONColumnsWithMetadata.

```csharp
var reader = ClickHouseJsonColumnsReader.WithMetadata();
await foreach (var batch in reader.ToArrowBatchesAsync("export.json"))
{
    using (batch)
        Console.WriteLine(batch.Length);
}
```

Use `ToArrowStreamAsync` to inspect the Arrow schema before reading batches.
Dispose the returned stream and each batch. Supplied .NET streams stay open by
default. ClickHouse decodes each value into Arrow buffers as it reads and produces
one batch per document. JSONColumnsWithMetadata requires `meta` before `data`.
Integration JSON buffers one fixture batch at a time and requires `schema`
before `batches`.

Integration JSON supports primitive numeric types, booleans, UTF-8, binary and
fixed-size binary, dates, times, timestamps, durations, Decimal128, Decimal256,
and null. ClickHouse metadata maps booleans, 8- through 64-bit integers, floats,
strings, fixed strings, dates, decimals, and single-level arrays of supported
types. `Nullable` controls field nullability. `LowCardinality` uses its underlying
value type. Unsupported types are reported with the field name.

Use `ToLazyArrowStream` or `ToLazyArrowStreamAsync` to decode only the columns
you access. Both readers expose the schema before reading records.

```csharp
using var stream = await reader.ToLazyArrowStreamAsync("export.json");
while (await stream.ReadNextRecordAsync() is { } record)
{
    using (record)
        Console.WriteLine(record.Column("id").Length);
}
```

For a file path, lazy records share the original read-only handle and retain
a packed index of value byte boundaries. The index covers every column and
lets the first `Column` call read and decode the requested values directly.
Keep the source bytes stable while any record is alive. For a supplied stream,
each record stages its raw columns in a temporary file. Records can outlive the
input reader in either case. The first `Column` call can perform synchronous I/O
and report conversion errors. Later calls return the cached array. Dispose each
record to release its arrays and backing storage.

## First example

```sh
dotnet add package PocketCsvReader
```

```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
// 1: Ada
```

See [output models](https://seddryck.github.io/PocketCsvReader/docs/output-models/), [reader filtering](https://seddryck.github.io/PocketCsvReader/docs/reader-filtering/), [object mapping](https://seddryck.github.io/PocketCsvReader/docs/mapper-object-builder/), [async reading](https://seddryck.github.io/PocketCsvReader/docs/async-reading/), [inference](https://seddryck.github.io/PocketCsvReader/docs/schema-inference/), and [performance guidance](https://seddryck.github.io/PocketCsvReader/docs/parser-optimizations/) for complete behavior and limitations.

Shared ownership, null, conversion, and performance contracts have their authoritative home in the documentation rather than duplicated README sections.
