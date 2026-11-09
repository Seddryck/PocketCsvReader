---
title: JSON and JSONC
tags: [formats]
---

## Complete JSON example

Input is a top-level array, `[{"id":1,"name":"Ada"}]`. Each array element becomes a row. Install `PocketCsvReader.Json`.

<!-- example: json-array -->
```csharp
using System.Text;
using PocketCsvReader.Json;

using var stream = new MemoryStream(Encoding.UTF8.GetBytes("[{\"id\":1,\"name\":\"Ada\"}]"));
using var reader = new JsonReader().ToDataReader(stream);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(reader.GetOrdinal("id"))}: {reader.GetString(reader.GetOrdinal("name"))}");
```

<!-- output: json-array -->
```text
1: Ada
```

PocketCsvReader provides separate extension packages for standard JSON documents and newline-delimited JSON streams. Both default to the same strict JSON grammar and expose records through the usual `IDataReader`, string-array, and `DataTable` entry points.

## Standard JSON documents

Install `PocketCsvReader.Json` when the input is one JSON document:

```bash
dotnet add package PocketCsvReader.Json
```

```csharp
using PocketCsvReader.Json;

using var reader = new JsonReader().ToDataReader("people.json");
while (reader.Read())
{
    var id = reader.GetInt32(reader.GetOrdinal("id"));
    var name = reader.GetString(reader.GetOrdinal("name"));
}
```

A root object produces one labeled row. A root array produces one row per element. Object elements expose their properties as labeled fields; primitive, array, and null elements use field ordinal zero. The reader accepts pretty printing and the four JSON whitespace characters, and it streams array elements without loading the complete document.

JSON is strict by default: comments, custom record separators, trailing commas, and content after the root value are rejected.

When only part of each object is needed, configure a projection. Unselected properties are validated but are not materialized, and selected properties are exposed in projection order regardless of their input order:

```csharp
using PocketCsvReader.Json.Configuration;

var json = new JsonReaderBuilder()
    .WithProjection(projection => projection
        .Property("name")
        .Property("amount")
        .Property("count"))
    .Build();
```

Projection avoids building field metadata and nested field trees for properties you will not use. The complete JSON value is still read and validated. Use `WithOrderedProjection` only when the selected properties are guaranteed to occur in projection order: it additionally replaces general name lookup with comparison against the next expected selected name. A record whose selected properties are reordered is rejected. See [Why projection improves performance](../json-projection/#why-projection-improves-performance) for a worked example and the limits of these savings.

Use `JsonReaderBuilder.WithStableFieldLayout()` for trusted object records with the same recursive structure and property order. This works with standalone objects, objects streamed from arrays, and objects selected by a record path. You can combine it with either projection mode. See [Stable field layout](../json-projection/#stable-field-layout).

## JSON with comments (JSONC)

Opt in to JSONC using the existing `PocketCsvReader.Json` package:

```csharp
var json = new JsonReaderBuilder()
    .WithJsonc()
    .WithRecordPath("$.customers[*]")
    .Build();

using var rows = json.ToDataReader("customers.jsonc");
```

```jsonc
{
  // Customers
  "customers": [
    { "id": 1, "name": "Alice", },
    /* Second customer */
    { "id": 2, "name": "Bob", },
  ],
}
```

This returns the same two rows as strict JSON without comments or trailing commas. `WithJsonc()` enables both `//` line comments (through newline or EOF) and `/* ... */` block comments, plus a single trailing comma before `}` or `]`. Comments may appear anywhere whitespace is allowed, including before and after the root. Comment markers inside strings remain literal text; block comments do not nest. Malformed commas, incomplete block comments, invalid strings/numbers, and additional root values still throw `InvalidDataException` with position information.

Profiles expose independent, read-only `AllowComments` and `AllowTrailingCommas` settings. For example, `new JsonProfile(allowComments: true)` accepts comments while continuing to reject trailing commas, and `new JsonProfile(allowTrailingCommas: true)` permits trailing commas while continuing to reject comments. Set both to `true` for JSONC. The builder offers `WithComments(bool allowComments = true)` and `WithTrailingCommas(bool allowTrailingCommas = true)`; `WithJsonc()` is shorthand for enabling both. Both settings default to `false`, and either can be disabled again after `WithJsonc()`. The existing profile constructor remains available. The file extension never changes the dialect automatically.

JSONC works with root objects, streamed top-level arrays, nested values, normal and ordered projection, record paths, and stable field layout for object records. It supports synchronous and asynchronous reads. Framing retains comment state across buffer boundaries and preserves offsets into the original input. Raw composite values retain their original comments and commas. As with strict JSON, record-path selection captures the complete document before enumerating matches. Without a record path, top-level array elements stream independently.

NDJSON retains its existing line framing and optional comment configuration. Its JSON values remain strict; `WithJsonc()` applies only to the JSON reader.

## Newline-delimited JSON

See [NDJSON framing and configuration](../ndjson/) for NDJSON projection and stable field layout.

## Select nested nodes as records

The shared syntax, examples and memory behavior now live in [Select JSON nodes as rows](../json-record-paths/).
