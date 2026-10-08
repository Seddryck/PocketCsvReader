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

This is a trusted-input assertion. The first object establishes the property labels and ordinal mapping. Later objects must have the same number of top-level properties, but their names are consumed only as JSON syntax and are not decoded or compared. A missing or additional property throws `InvalidDataException`; a rename or reorder with the same property count can map values to the wrong ordinal. JSON syntax, escaped names, nested values, comments, synchronous and asynchronous reads, and projections retain their normal behavior. The option does not assume stable value types: conversion strategies are cached only after a schema or typed accessor establishes the target type for an ordinal.
