---
title: Schemas, formats, and cultures
tags: [configuration]
---

<span id="benefits-of-using-a-schema"></span>
<span id="conclusion"></span>
<span id="creating-a-named-schema"></span>
<span id="creating-an-indexed-schema"></span>
<span id="custom-field"></span>
<span id="defining-a-custom-field-with-formatting"></span>
<span id="overview"></span>

A schema controls the type and conversion settings used by the data reader. It does not select source columns or bind CSV constructor parameters by name. [Object mapping](../mapper-object-builder/) is a separate path; [JSON projection](../json-projection/) selects properties.

## Defining a schema

An indexed schema follows column positions. A named schema follows reader field names, which is useful when labeled records change order. Name matching is case-sensitive. A schema validates values when conversion is requested; it does not guarantee the whole file is valid before reading.

<!-- example: schema-formats -->
```csharp
using System.Globalization;
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithDelimiter(';').WithHeader().WithLineTerminator("\n"))
    .WithSchema(s => s.Named()
        .WithTemporalField<DateOnly>("Date", f => f.WithFormat("dd/MM/yyyy"))
        .WithNumberField<decimal>("Amount", f => f.WithFormat(n =>
            n.WithDecimalChar(',').WithoutGroupChar())))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Date;Amount\n30/10/2026;149,95\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
    Console.WriteLine(FormattableString.Invariant(
        $"{reader.GetFieldValue<DateOnly>(0):yyyy-MM-dd}: {reader.GetDecimal(1):F2}"));
```

<!-- output: schema-formats -->
```text
2026-10-30: 149.95
```

For positions, use `new SchemaDescriptorBuilder().Indexed().WithField<int>().WithField<string>().Build()`. Supply the result to `WithSchema`. Named schemas use `Named().WithField<int>("Id")`.

## Using field formatting and format descriptors

Use `WithTemporalField<T>` for a temporal pattern, `WithNumberField<T>` for decimal/group separators, and `WithIntegerField<T>` for integer settings. Use `WithField<T>` with `WithFormat(pattern, formatProvider)` for custom parser formats. Formats use their configured provider; built-in schema builders supply invariant defaults. A fixed date format avoids relying on machine culture.

### Numeric formatting

`NumberFieldDescriptorBuilder.WithFormat` receives a `NumberFormatDescriptorBuilder`. Set `WithDecimalChar`, `WithGroupChar`, or `WithoutGroupChar` inside that callback. The type is named **Number**, not Numeric.

### Migrating from WithCustomField

**Deprecated.** `WithCustomField` still works and now emits a compiler warning. It will be removed in PocketCsvReader 3.0. Replace it with `WithField` in named and indexed schemas. Keep the same type, column name or position, and inline configuration callback.

For a named column, use `WithField<YearMonth>("Month", f => f.WithFormat("yyyy-MM"))`. For an indexed column, use `WithField<YearMonth>(f => f.WithFormat("yyyy-MM"))`. The optional second argument to `WithFormat` supplies the format provider to parsers that support it. A field callback declared separately as `Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>` should use `Func<FieldDescriptorBuilder, FieldDescriptorBuilder>` when passed to `WithField`.

## Providing a custom parser

Register an explicitly typed span lambda on a field, for example `WithField<int>(f => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text, CultureInfo.InvariantCulture)))`. For a type shared by multiple fields, use a [runtime parser](../fluent-api-runtime-parsers/). Field parsers take precedence over field formats and runtime parsers. A field format can take precedence over a runtime type parser, so inspect field settings when a runtime override appears ineffective.

Custom types must be defined by the application. For a coordinate type, define its public constructor and conversion logic explicitly rather than copying an undefined `Point` snippet. [Custom parsers](../fluent-api-runtime-parsers/) contains a complete custom-type example.

## Arrays

Configure array syntax in the [CSV dialect](../csv-dialect-descriptor/#arrays), then declare a one-dimensional CLR array such as `WithField<int[]>("Scores")`. `GetValue`, indexers and `GetValues` return a typed array through the element-parser path. `GetArray<T>` also works without a whole-array schema declaration. A whole-array custom parser takes precedence over element parsing.

Declaring an array does not turn scalar text into an array. Enumerable types are not array declarations; multidimensional CLR arrays are unsupported. Null elements and conversion failures follow the relevant array getters. JSON arrays have their own source grammar; see [JSON](../json-and-ndjson/) and [NDJSON](../ndjson/).

## Hex and Base64 fields

Choose the decoder that matches the encoding used by the CSV producer. PocketCsvReader does not guess the encoding. A plain `WithField<byte[]>("Payload")` uses the dialect’s array syntax, such as `[0|255|44]` with the array delimiter `|` and brackets configured.

| Field configuration | Accepted text |
| --- | --- |
| `f => f.FromHex()` | Two hex digits per byte, in upper or lower case. Enter `00FF2C` as a continuous sequence without a `0x` prefix or separators. |
| `f => f.FromBase64()` | Standard Base64 using `+` and `/`, with `=` padding when required. The decoder ignores spaces, tabs, and line breaks present in the field value. Use a custom parser for Base64url. |

Both shortcuts are available on the shared builder and require a field declared as `byte[]`. Calling either shortcut for another field type throws `InvalidOperationException` during schema configuration. For an indexed schema, use `Indexed().WithField<byte[]>(f => f.FromHex())` or the corresponding `FromBase64()` form.

<!-- example: binary-schema-fields -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithDelimiter(',').WithHeader().WithLineTerminator("\n"))
    .WithSchema(s => s.Named()
        .WithField<byte[]>("HexPayload", f => f.FromHex())
        .WithField<byte[]>("Base64Payload", f => f.FromBase64()))
    .Build();

using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
    "HexPayload,Base64Payload\n00FF2C,AP8s\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
{
    Console.WriteLine(Convert.ToHexString((byte[])reader["HexPayload"]));
    Console.WriteLine(Convert.ToHexString((byte[])reader["Base64Payload"]));
}
```

<!-- output: binary-schema-fields -->
```text
00FF2C
00FF2C
```

Each shortcut registers a field parser for the whole cell. Once on a row, use `IsValid("HexPayload")` to check whether the cell can be decoded. Reading malformed encoded text through the indexer or `GetValue` throws `FormatException`. An empty string passed to either decoder yields an empty byte array. Values interpreted as null or missing follow the reader’s [null and missing value rules](../sequences/).

The decoder returns raw bytes. For a payload such as Well-Known Binary (WKB), use a WKB reader to interpret those bytes as geometry.

## Validation and inference

Use [IsValid](../validation-and-errors/#validate-a-cell) to check a cell against its current declared/inferred type. Use [inference](../schema-inference/) to discover candidate settings from a sample, then review diagnostics before applying them. [Null and missing values](../sequences/) explains the different cases that a schema alone does not normalize.
