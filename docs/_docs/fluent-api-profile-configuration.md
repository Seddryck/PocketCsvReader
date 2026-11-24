---
title: Profiles and builders
tags: [configuration]
---

<span id="available-methods-of-dialectdescriptorbuilder"></span>
<span id="dialect-descriptor"></span>
<span id="usage-of-csvreaderbuilder"></span>
<span id="usage-of-dialectdescriptorbuilder"></span>

A reader profile combines independent settings. Use a format-specific builder so its syntax defaults and supported options are applied before reading.

| Part | Responsibility | Guide |
| --- | --- | --- |
| Dialect/format | Record boundaries, quoting, labels | [Format guides](../why-pocketcsvreader/#supported-inputs) |
| Schema | Field names, types, cultures and formats | [Schemas](../fluent-api-schema/) |
| Resource | Encoding, compression, replacement sequences | [Input initialization](../fluent-api-resource/), [compression](../compressed-files/), [nulls](../sequences/) |
| Filters | Conditions applied to exposed columns before materialization | [Reader filtering](../reader-filtering/) |
| Runtime parsers | Conversion shared by runtime type | [Custom parsers](../fluent-api-runtime-parsers/) |
| Parser options | Buffering and supported format options | [Tuning](../parser-optimizations/) |

<!-- example: configured-reader -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithSchema(s => s.Named().WithField<int>("Id").WithField<string>("Name"))
    .WithResource(r => r.WithEncoding("utf-8"))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n7,Ada\n"));
foreach (var row in csv.ToDictionaries(stream))
    Console.WriteLine($"{row["Id"]}: {row["Name"]}");
```

<!-- output: configured-reader -->
```text
7: Ada
```

Builder overloads accept configuration callbacks and descriptors. CSV uses `WithDialect`; other builders expose their own format settings. Configure profiles before constructing/initializing a reader. Supplying a shared options record does not imply every format supports every option.

## CSV column projection

Use `WithProjection("CustomerId", "Country", "Amount")` to select columns by exact header name and set their output order. Use zero-based source ordinals, such as `WithProjection(3, 2, 0)`, for headerless files or when a source name occurs more than once.

The reader resolves the projection against each file’s effective header, including files that contain only headers. A batch configured with a shared header uses that header for each file. Missing or ambiguous names and ordinals outside the source width raise `InvalidDataException`.

Schema definitions and filters still refer to source columns. Output getters and metadata use projected ordinals. A generated row number remains at the front of the output, and projection ordinals count only CSV source columns.

## Generated row-number column

`WithRowNumber()` prepends an `int` column named `rowNumber`; `WithRowNumber("recordId")` selects another name, and `WithoutRowNumber()` disables it. Direct profiles use `RowNumber = new RowNumberOptions()` or null. See [batch reading and row numbering](../batch-reading/#generated-row-numbers) for numbering, headers, collisions, and object mapping.

## Class CsvReaderBuilder

The current method is `WithDialect(...)`, followed by `Build()`. Dialect methods and defaults are documented in [CSV and TSV](../csv-dialect-descriptor/).

## Class DialectDescriptorBuilder

Use this builder to produce a `DialectDescriptor`, or pass its callback directly to `WithDialect`. See the [dialect reference](../csv-dialect-descriptor/#dialect-reference) and [configuration validation](../csv-dialect-descriptor/#configuration-errors).
