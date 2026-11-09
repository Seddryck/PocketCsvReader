---
title: NDJSON
tags: [formats]
---

## Complete NDJSON example

Input consists of one JSON object per LF-terminated record. Install `PocketCsvReader.Ndjson`.

<!-- example: ndjson-events -->
```csharp
using System.Text;
using PocketCsvReader.Ndjson.Configuration;

var ndjson = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
    "{\"event\":\"accepted\",\"id\":1}\n{\"id\":2,\"event\":\"pending\"}\n"));
using var reader = ndjson.ToDataReader(stream);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(reader.GetOrdinal("id"))}: {reader.GetString(reader.GetOrdinal("event"))}");
```

<!-- output: ndjson-events -->
```text
1: accepted
2: pending
```

## Newline-delimited JSON

Install `PocketCsvReader.Ndjson` when the input contains a sequence of complete JSON values separated by line terminators:

```bash
dotnet add package PocketCsvReader.Ndjson
```

```csharp
using PocketCsvReader.Ndjson;

using var reader = new NdjsonReader().ToDataReader("events.ndjson");
while (reader.Read())
{
    var eventName = reader.GetString(reader.GetOrdinal("event"));
}
```

NDJSON keeps its format-specific framing options, including custom line terminators, blank-line handling, and optional comments. Use `PocketCsvReader.Json` for a pretty-printed array document; use `PocketCsvReader.Ndjson` for independently framed JSON values.

NDJSON also supports order-independent projection:

```csharp
using PocketCsvReader.Ndjson.Configuration;

var ndjson = new NdjsonReaderBuilder()
    .WithProjection(projection => projection
        .Property("event")
        .Property("timestamp"))
    .Build();

using var reader = ndjson.ToDataReader("events.ndjson");
```

Each NDJSON record must be a JSON object containing every projected property. Additional properties may appear anywhere and remain fully validated. Projection does not change custom line terminators, blank-line handling, or comment behavior.

When the selected properties have a guaranteed input order, `WithOrderedProjection` provides the same stricter contract as the JSON reader:

```csharp
var ndjson = new NdjsonReaderBuilder()
    .WithOrderedProjection(projection => projection
        .Property("event")
        .Property("timestamp"))
    .Build();
```

Unselected properties may occur before, after, or between selected properties, but the selected properties themselves must occur in configuration order. A reordered selected property is rejected.

With stable field layout disabled, both projection modes avoid representing unused fields while validating the full record. Ordered projection uses the configured order to find selected fields. See [Why projection improves performance](../json-projection/#why-projection-improves-performance) for a comparison with unprojected reading.

For trusted object records with a consistent layout, use `NdjsonReaderBuilder.WithStableFieldLayout()` with or without projection. See [Stable field layout](../json-projection/#stable-field-layout) for the source layout contract and the effect of same-count renames and reordering.

## Boundaries and values

Install the package shown above; the builder namespace is `PocketCsvReader.Ndjson.Configuration`. Configure `.WithDialect(d => d.WithLineTerminator("\n"))` for LF input or the appropriate explicit terminator for your producer. A final complete value need not end in a terminator. Blank-line/comment handling follows the NDJSON dialect; invalid JSON remains an error. The JSON reader's `WithJsonc()` is not an NDJSON mode.

JSON null, quoted strings, numbers, arrays and nested objects retain their source semantics. Strings are decoded lazily when requested; skipped fields do not require ordinary getter string materialization. Use [optional labels](../validation-and-errors/#optional-fields-and-name-based-null-checks) for changing object shapes, [output models](../output-models/) for results, and [source positions](../source-positions/) for diagnostics.

## Select nested nodes as records

See [Select JSON nodes as rows](../json-record-paths/) for the supported path subset and per-record buffering. See [JSON projection and stable field layout](../json-projection/) for performance and parser behavior.
