---
title: JSON and NDJSON
tags: [formats]
---

PocketCsvReader provides separate extension packages for standard JSON documents and newline-delimited JSON streams. Both reuse the same strict JSON grammar and expose records through the usual `IDataReader`, string-array, and `DataTable` entry points.

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

JSON is strict: comments, custom record separators, trailing commas, and content after the root value are rejected.

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

Use `WithOrderedProjection` only when the selected properties are guaranteed to occur in projection order. It avoids general property lookup, but a record whose selected properties are reordered is rejected.

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

When every object is guaranteed to have the same properties in the same order, opt in to stable-shape parsing:

```csharp
var ndjson = new NdjsonReaderBuilder()
    .WithStableObjectShape()
    .Build();
```

This is a trusted-input assertion. The first object establishes the recursive JSON structure, including object property counts, array lengths, property labels, and ordinal mappings at every level. Later objects must retain that structure, but their property names are consumed only as JSON syntax and are not decoded or compared. Missing or additional properties, array-length changes, and scalar/container changes throw `InvalidDataException`; a rename or reorder with the same property count can map values to the wrong ordinal. JSON syntax, escaped names, comments, synchronous and asynchronous reads, and projections retain their normal behavior. Scalar leaf types remain dynamic: conversion strategies are cached only after a schema or typed accessor establishes the target type for an ordinal.

## Select nested nodes as records

Both builders support `WithRecordPath(string path)`:

```csharp
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

var json = new JsonReaderBuilder()
    .WithRecordPath("$.child[*].grandchildren[*]")
    .Build();

var ndjson = new NdjsonReaderBuilder()
    .WithRecordPath("$.child[*].grandchildren[*]")
    .Build();

using var reader = json.ToDataReader(stream);
while (reader.Read())
{
    var grandchild = reader.GetString(0);
}
```

For `{"child":[{"grandchildren":["Ada","Grace"]},{"grandchildren":["Linus"]}]}`, this path produces three rows: `Ada`, `Grace`, and `Linus`. Using `$.child[*].grandchildren` instead produces two rows, each with one array field; call `GetArray(0)` to read each array. Arrays are never implicitly flattened.

The supported subset of [RFC 9535 JSONPath](https://www.rfc-editor.org/rfc/rfc9535.html) is:

- `$` identifies the complete JSON document, or each complete value in an NDJSON stream. `$` alone selects that value as one row, including a root array.
- `.name` selects an object member. Names start with an ASCII letter, underscore, or non-ASCII Unicode scalar, followed by those characters or ASCII digits.
- `['name']` or `["name"]` selects a literal member name, including empty names and punctuation, such as `$['child.with.dot']`. Quoted names support `\\`, `\/`, the matching escaped quote, `\b`, `\f`, `\n`, `\r`, `\t`, and `\uXXXX` (including surrogate pairs).
- `[*]` selects array elements in document order and may be repeated. It selects nothing from objects or scalar values.

No whitespace is accepted outside quoted member names. Indexes, filters, recursive descent, unions, slices, and `.*` are unsupported. Member comparisons are case-sensitive and do not normalize Unicode. Invalid or unsupported syntax throws `ArgumentException`, including the path, when the builder's `Build()` method or profile constructor runs. Profiles also accept the optional `recordPath` constructor argument.

Each match becomes exactly one row. Objects expose their members as labeled fields; strings, numbers, booleans, arrays, and null expose one unnamed field at ordinal zero. Strings are decoded by the existing getters and null retains database-null semantics. Schema, custom parsers, and projection apply to each selected row; properties outside the selected node are absent. NDJSON's stable-object-shape option, when enabled, applies to the selected objects.

Missing members and non-array wildcard inputs produce no matches; a path with no matches gives an empty reader. Duplicate occurrences of a member name used by the path throw `InvalidDataException` when that object is visited. Malformed JSON throws `InvalidDataException` with position information, including in unselected branches.

Selection uses the existing JSON parser and buffered framing. It captures and parses the complete document for JSON, or one complete framed value at a time for NDJSON, then lazily enumerates matches without collecting a result list. The captured input stays alive until its matches have been consumed. `ReadAsync` uses asynchronous stream reads and follows the same selection rules as `Read`.

Omitting `WithRecordPath` preserves the existing root-object and root-array behavior, including streaming top-level JSON array elements.
