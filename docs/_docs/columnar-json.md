---
title: Columnar JSON
tags: [formats, arrow]
---

Columnar JSON groups values by column. For example, `{"id":[1,2],"name":["Ada","Lin"]}` describes two rows. Use `PocketCsvReader.Arrow.ColumnarJson` to read these formats directly as Apache Arrow record batches. For arrays of row objects or one JSON value per line, see [JSON and JSONC](../json-and-ndjson/) or [NDJSON](../ndjson/).

```sh
dotnet add package PocketCsvReader.Arrow.ColumnarJson
```

| Input | Shape | Reader |
| --- | --- | --- |
| ClickHouse JSONColumns | Root object with one array per column | `new ClickHouseJsonColumnsReader(schema)` |
| ClickHouse JSONColumnsWithMetadata | `meta` array followed by a `data` object | `ClickHouseJsonColumnsReader.WithMetadata()` |
| Apache Arrow Integration JSON | `schema` followed by fixture `batches` | `new ArrowIntegrationJsonReader()` |

## ClickHouse JSONColumns

Supply the Arrow schema explicitly. The reader matches column names and emits columns in schema order. Each declared column must appear once, and all arrays must have the same length. JSON `null` is accepted for nullable fields.

<!-- example: clickhouse-jsoncolumns -->
```csharp
using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using PocketCsvReader.Arrow;
using PocketCsvReader.Arrow.ColumnarJson;

var schema = new Schema.Builder()
    .Field(new Field("id", Int32Type.Default, false))
    .Field(new Field("name", StringType.Default, true))
    .Build();
var reader = new ClickHouseJsonColumnsReader(schema);
using var input = new MemoryStream(Encoding.UTF8.GetBytes("""{"name":["Ada",null],"id":[1,2]}"""));

foreach (var batch in reader.ToArrowBatches(input))
{
    using (batch)
        Console.WriteLine($"{batch.Schema.FieldsList[0].Name} is first, {batch.Length} rows");
}
```

<!-- output: clickhouse-jsoncolumns -->
```text
id is first, 2 rows
```

## ClickHouse JSONColumnsWithMetadata

Place `meta` before `data`. The reader derives the Arrow schema in `meta` order and checks that `rows` agrees with the column lengths when present.

<!-- example: clickhouse-jsoncolumns-with-metadata -->
```csharp
using System.Text;
using PocketCsvReader.Arrow.ColumnarJson;

using var input = new MemoryStream(Encoding.UTF8.GetBytes("""
    {
      "meta": [{"name":"id","type":"UInt32"}],
      "data": {"id":[1,2]},
      "rows": 2
    }
    """));
var reader = ClickHouseJsonColumnsReader.WithMetadata();
using var stream = await reader.ToArrowStreamAsync(input);
Console.WriteLine(stream.Schema.FieldsList[0].Name);
using var batch = await stream.ReadNextRecordBatchAsync();
Console.WriteLine(batch?.Length);
```

<!-- output: clickhouse-jsoncolumns-with-metadata -->
```text
id
2
```

Metadata maps `Bool`, signed and unsigned 8- through 64-bit integers, floats, `String`, UUIDs, IP addresses, fixed strings, dates, decimals, and single-level arrays of supported types. `Nullable(T)` controls nullability, and `LowCardinality(T)` uses the mapping for `T`. Unsupported declarations such as `DateTime` or `Map` produce an error naming the column.

Both ClickHouse variants produce one batch per document, including a batch of length zero for empty columns. The eager reader decodes values into Arrow arrays as it reads.

## Apache Arrow Integration JSON

Use this reader for Arrow JSON test fixtures. Place `schema` before `batches`. Each fixture batch becomes one Arrow batch. `VALIDITY` marks present and null values, and variable-width data such as UTF-8 uses `OFFSET`.

<!-- example: arrow-integration-json -->
```csharp
using System.Text;
using PocketCsvReader.Arrow;
using PocketCsvReader.Arrow.ColumnarJson;

using var input = new MemoryStream(Encoding.UTF8.GetBytes("""
    {
      "schema": {
        "fields": [{"name":"id","nullable":false,"type":{"name":"int","isSigned":true,"bitWidth":32}}]
      },
      "batches": [
        {"count":2,"columns":[{"name":"id","count":2,"VALIDITY":[1,1],"DATA":[1,2]}]}
      ]
    }
    """));
var reader = new ArrowIntegrationJsonReader();
await foreach (var batch in reader.ToArrowBatchesAsync(input))
{
    using (batch)
        Console.WriteLine(batch.Length);
}
```

<!-- output: arrow-integration-json -->
```text
2
```

The reader supports primitive numeric types, booleans, UTF-8, binary and fixed-size binary, dates, times, timestamps, durations, Decimal128, Decimal256, and null. Nested fields and dictionary encoding are unsupported. The eager reader buffers one fixture batch at a time.

## Streams and lazy column access

Use `ToArrowStream` or `ToArrowStreamAsync` to inspect the schema before reading batches. Dispose the returned stream and each batch. Reader-created file streams are owned by the reader. Supplied streams stay open by default. Pass `leaveOpen: false` when configuring the reader to transfer ownership.

Use `ToLazyArrowStream` or `ToLazyArrowStreamAsync` to decode only the columns you access. The stream exposes the schema before the first record. Each record caches a column on first access.

```csharp
var reader = ClickHouseJsonColumnsReader.WithMetadata();
using var stream = await reader.ToLazyArrowStreamAsync("export.json");
while (await stream.ReadNextRecordAsync() is { } record)
{
    using (record)
        Console.WriteLine(record.Column("id").Length);
}
```

For a file path, lazy records share the source file handle and index each column's value boundaries. Keep the source bytes stable while any record is alive. For a supplied stream, each lazy record stages its raw columns in a temporary file. Records can outlive their input reader in either case. The first `Column` call can perform synchronous I/O and report conversion errors. Dispose each record to release its arrays and backing storage.
