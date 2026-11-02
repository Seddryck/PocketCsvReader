---
title: Choose an output model
tags: [usage]
---

Choose the row shape and lifetime you need before choosing an optimization. Most readers expose shared output APIs, but CSV's span-based object mapper and its `ToAsync<T>` API differ from data-reader mappers on extension readers.

## Compare outputs

| API | Contents | Streaming | Ownership |
| --- | --- | --- | --- |
| `ToDataReader` | Current row, typed/raw getters | Yes | Dispose the reader; it owns the input unless explicitly constructed otherwise |
| `ToDbDataReader` | Standard ADO.NET adapter | Yes, including async reads | Dispose the adapter |
| `To<T>` | Constructor-mapped or custom-mapped objects | Yes | See format-specific mapping and stream rules |
| CSV `ToAsync<T>` | Async mapped objects | Yes | File closes; caller-provided stream remains open |
| `ToArrayString` | `string?[]` in current field order | Yes | Enumerator disposes reader and input |
| `ToObjectArrays` | `object?[]`, parsed values | Yes | Enumerator disposes reader and input |
| `ToDictionaries` | Read-only string-keyed parsed values | Yes | Enumerator disposes reader and input |
| `ToDynamic` | `ExpandoObject` with parsed members | Yes | Enumerator disposes reader and input |
| `ToDataTable` | Complete in-memory table | No | Reads/disposes input during the call |

`ToDataTable` currently stores ordinary columns as strings; a generated CSV row-number column is an integer. It is not a typed equivalent of `ToObjectArrays`. See [stream lifetimes](../stream-lifetimes/) for lazy versus eager creation and early exits.

## Parsed rows

<!-- example: parsed-rows -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithSchema(s => s.Named().WithField<int>("Id").WithField<string>("Name"))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n"));
foreach (var row in csv.ToDictionaries(stream))
    Console.WriteLine($"{row["Id"].GetType().Name}: {row["Name"]}");
```

<!-- output: parsed-rows -->
```text
Int32: Ada
```

Use `ToObjectArrays` for the same parsed values by position, `ToArrayString` for strings, or `ToDynamic` for member access. Fields without an explicit type remain text. Parsed arrays/dictionaries/dynamic rows normalize database nulls to C# null; empty strings remain empty unless the format's configured value handling changes them.

## Names, shapes, and duplicate fields

Dictionaries use ordinal, case-sensitive keys. Duplicate names throw `InvalidDataException`; dynamic rows throw `DuplicateNameException`. Positional arrays preserve duplicate columns. Tables reject duplicate column names through their column collection. These distinctions avoid silently overwriting values.

For labeled formats, each dictionary or array reflects that current record's field count and order. Do not cache ordinals across changing shapes. Tabular consumers require a consistent shape; `ToDataTable` establishes columns from the first record. Incomplete and extra CSV fields retain the underlying reader's width behavior; materializers do not universally pad missing values.

Returned row arrays, strings, dictionaries, and dynamic objects are independently owned. Borrowed parser views expire on the next read; see [buffering](../buffering-and-benchmarks/). All materializers can throw during enumeration when conversion fails. Use `foreach` or dispose enumerators explicitly on early exit.

For typed objects use [object mapping](../mapper-object-builder/); for asynchronous consumption use [async reading](../async-reading/); for columnar analytics use [Arrow batches](../apache-arrow/).
