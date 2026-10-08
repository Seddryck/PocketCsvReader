---
title: Apache Arrow record batches
tags: [analytics, arrow]
---

Install the optional Arrow adapter when records need to flow into a columnar analytics pipeline. The core package does not depend on Apache Arrow.

```bash
dotnet add package PocketCsvReader.Arrow
```

Configure a stable PocketCsvReader schema, import the adapter namespace, and enumerate the batches:

```csharp
using PocketCsvReader.Arrow;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(dialect => dialect
        .WithDelimiter(',')
        .WithHeader())
    .WithSchema(schema => schema.Named()
        .WithField<int>("customer_id")
        .WithField<string>("country")
        .WithNumberField<decimal>("revenue"))
    .Build();

foreach (var batch in csv.ToArrowBatches("sales.csv", batchSize: 32_768))
{
    using (batch)
    {
        // Send the RecordBatch to an Arrow-aware analytics engine.
    }
}
```

`ToArrowBatchesAsync` provides genuine asynchronous input reads and accepts a cancellation token:

```csharp
await foreach (var batch in csv.ToArrowBatchesAsync(
    "sales.csv",
    batchSize: 32_768,
    cancellationToken: cancellationToken))
{
    using (batch)
    {
        await ConsumeAsync(batch, cancellationToken);
    }
}
```

The APIs are extensions on the shared flat-file reader abstraction, so the same calls work with CSV, JSON, NDJSON, fixed-width, key-value, and web-log readers. Each batch contains at most `batchSize` rows; the final partial batch is emitted, while empty input emits no batch. Memory use is bounded by one batch plus the parser buffers.

Each `RecordBatch` owns Arrow buffers and must be disposed by the consumer. Enumeration disposes the PocketCsvReader data reader. Stream overloads follow that reader's ownership behavior and therefore dispose the supplied stream when enumeration ends, fails, or is cancelled.

## Type mappings

| PocketCsvReader runtime type | Apache Arrow type |
|---|---|
| `bool` | `Boolean` |
| signed and unsigned integer primitives | matching-width signed or unsigned integer |
| `float`, `double` | `Float`, `Double` |
| `decimal` | `Decimal128(38, 18)` |
| `string`, `char`, `Guid` | `String` |
| `byte[]` | `Binary` |
| `DateOnly` | `Date32` |
| `DateTime`, `DateTimeOffset` | UTC `Timestamp` in microseconds |
| `TimeOnly` | `Time64` in nanoseconds |

Null input values are appended to Arrow validity bitmaps. Unsupported and untyped `object` fields fail before the first batch is emitted with an error identifying the field. Configure those fields with a supported runtime type or a custom PocketCsvReader parser.
