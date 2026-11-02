---
title: Parser optimizations
tags: [configuration]
---
The `ParserOptimizationOptions` record provides settings shared by several readers. The defaults and descriptions below refer to CSV unless stated otherwise. Other formats use their own framing and syntax rules; accepting the same options record does not mean every CSV setting changes their behavior. See [JSON, NDJSON, and key-value readers](#json-ndjson-and-key-value-readers) for their defaults and format-specific optimizations.

## Options and Their Descriptions

### `NoTextQualifier`

- **Default Value:** `false`
- **Description:** Disables the use of text qualifiers (e.g., quotes around fields).
- **Impact:** 
  - When `true`, the parser will not handle fields enclosed in quotes. This can improve performance if the CSV does not use text qualifiers.
  - If the CSV contains quoted fields, enabling this might lead to incorrect parsing.

### `UnescapeChars`

- **Default Value:** `true`
- **Description:** Enables or disables unescaping of special characters (e.g., newline, delimiter, or escape sequences) within quoted fields.
- **Impact:** 
  - When `true`, escaped characters like `\n` are processed as their literal equivalents (e.g., `
`).
  - Set to `false` if unescaping is unnecessary, improving performance.

### `HandleSpecialValues`

- **Default Value:** `true`
- **Description:** Controls whether the parser recognizes and processes special values (e.g., null sequences or special keywords like "NULL").
- **Impact:**
  - When `true`, the parser converts defined special values (e.g., "NULL") into `null` or other intended values.
  - Disabling this can slightly improve performance when such conversions are unnecessary.

### `RowCountAtStart`

- **Default Value:** `false`
- **Description:** Determines whether the total row count is calculated at the start of parsing.
- **Impact:**
  - When `true`, the parser reads through the entire file once to determine the total number of rows before starting actual parsing.
  - Useful for progress tracking but can increase the initial processing time for large files.

### `ExtendIncompleteRecords`

- **Default Value:** `true`
- **Description:** Specifies whether incomplete rows (fewer columns than expected) should be extended with empty fields.
- **Impact:**
  - When `true`, incomplete rows are padded to match the expected number of columns.
  - Set to `false` to raise an error or skip such rows.

### `ReadAhead`

- **Default Value:** `true`
- **Description:** Enables prefetching of data to improve parsing performance.
- **Impact:**
  - When `true`, the parser reads ahead in the input stream to reduce I/O overhead.
  - Disabling this can save memory but might slow down parsing for large files.

### `BufferSize`

- **Default Value:** `4096` (4 KB)
- **Description:** The size of the buffer used for reading data from the input stream.
- **Impact:**
  - Larger buffer sizes can improve performance for large files but increase memory usage.
  - Smaller buffer sizes reduce memory usage but might slow down parsing.

### `PoolString`

- **Default Value:** `null`
- **Description:** Specifies a string pooling mechanism to optimize memory usage for repeated values.
- **Impact:**
  - Use this option to minimize memory consumption when dealing with CSV files containing many repeated strings.

### `LookupTableChar`

- **Default Value:** `true`
- **Description:** Determines whether a lookup table is used for character checks (e.g., delimiter or quote detection).
- **Impact:**
  - When `true`, a lookup table is used, which speeds up character checks during parsing.
  - Disabling this might slightly reduce parsing speed but save some memory.

## JSON, NDJSON, and key-value readers

The JSON, NDJSON, and LTSV/logfmt readers share the labeled-reader layer, but their framing and field-parsing strategies differ. The following optimizations are applied automatically unless the table identifies an opt-in API.

### Options for NDJSON, LTSV, and logfmt

NDJSON and key-value builders default to a 64 KB `BufferSize`, with `ReadAhead` and `RowCountAtStart` set to `false`. These readers consume input through reusable chunk buffers without the CSV double-buffer read-ahead or row-count pre-scan. NDJSON does not use those two flags to select its record source. LTSV/logfmt builders and profiles explicitly force both flags to `false`, even when requested as `true`.

`BufferSize` controls the input chunk size, rather than limiting record length. Records that cross chunk boundaries are consolidated in pooled storage. A record already contained in a buffer is read directly from that memory. Configure `PoolString` when you need string pooling, and use `.WithPositionTracking()` to opt into source-position tracking (disabled by default).

When supplying a new options record, set the intended values explicitly: `new ParserOptimizationOptions(BufferSize: 128 * 1024)` still has the record constructor's CSV default `ReadAhead = true`. For example:

```csharp
using PocketCsvReader;
using PocketCsvReader.Ndjson.Configuration;
using PocketCsvReader.KeyValue.Configuration;

var options = new ParserOptimizationOptions(
    BufferSize: 128 * 1024,
    ReadAhead: false,
    RowCountAtStart: false);

var ndjson = new NdjsonReaderBuilder().WithParserOptimizations(options).Build();
var ltsv = new LtsvReaderBuilder().WithParserOptimizations(options).Build();
var logfmt = new LogfmtReaderBuilder().WithParserOptimizations(options).Build();
```

CSV-specific settings such as `NoTextQualifier`, `LookupTableChar`, and `ExtendIncompleteRecords` do not configure these formats' grammar or record shape. JSON string escapes and logfmt quoted-value escapes are decoded by their format parsers; setting `UnescapeChars = false` does not disable that decoding. LTSV does not use CSV quoting or escape rules. These labeled readers do not apply the CSV sequence-substitution sanitizer, so `HandleSpecialValues` does not enable sequence replacements or disable recognition of JSON's native `null` token.

### Automatic storage and projection optimizations

| Optimization | JSON | NDJSON | LTSV/logfmt |
| --- | --- | --- | --- |
| Cache repeated label sequences and ordinal dictionaries | Yes | Yes | Yes |
| Keep synchronous current-record memory without a per-row wrapper | Yes | Yes | Yes |
| Read records through reusable chunk buffers | Yes | Yes | Yes |
| Project selected JSON properties | `WithProjection` | `WithProjection` | Not applicable |
| Reuse the projected `FieldSpan` buffer between records | Yes | Yes | Not applicable |
| Find projected fields while locating the record boundary | Yes | Yes, for eligible configurations described below | Not applicable |
| Stop property lookup after every projected field is found | Yes | Yes | Not applicable |
| Require projected properties in a known order | `WithOrderedProjection` | `WithOrderedProjection` | Not applicable |
| Trust and reuse one stable object shape | Not applicable | `WithStableObjectShape` | Not applicable |

### Repeated labeled row shapes

When rows repeat the same labels in the same order, their label strings and name-to-ordinal dictionary are reused. A different field count or label order is treated as another row shape. Up to 64 shapes are cached per data reader so highly heterogeneous input remains bounded.

### Why projection improves performance

Consider an event containing a few fields used by an application and a larger diagnostic payload:

```json
{"event":"payment","debug":{"trace":[1,2,3],"message":"details"},"timestamp":"2026-11-02T10:00:00Z","host":"worker-1","tags":["billing","retry"]}
```

If the application only needs `event` and `timestamp`, configure those two properties:

```csharp
var events = new NdjsonReaderBuilder()
    .WithProjection(p => p.Property("event").Property("timestamp"))
    .Build();
```

The same projection APIs are available on `JsonReaderBuilder`. They change the parser's work before the application reads a field:

| Work | Without projection | `WithProjection` | `WithOrderedProjection` |
| --- | --- | --- | --- |
| Read input and validate JSON syntax | Entire record | Entire record | Entire record |
| Build field metadata and nested field trees | All exposed properties | Selected properties only | Selected properties only |
| Match a property to an output column | Discover the record's labels | Look up its name among selected names | Compare its name with the next expected selected name |
| Search for selected names after all have been found | Not applicable | Stops | Stops |
| Allow selected properties in any input order | Yes | Yes | No |

`WithProjection` saves the work of constructing and maintaining field metadata for `debug`, `host`, and `tags`. The parser still checks their JSON syntax, but does not build the nested field trees that would expose their values. The selected fields use a reusable output buffer, and their configured names establish output order. This reduces metadata work and pressure on temporary storage. Skipping unselected values also avoids materialization that would otherwise be needed to expose them; ordinary string values can already be decoded lazily, so projection does not imply that every string was eagerly allocated without it.

Selecting fields in the reader is therefore different from calling `ToDataReader()` without a projection and only reading two columns. Typed access to unused fields may already be lazy, but the unprojected parser still discovers and represents their fields. Projection removes that earlier work. It is also different from applying LINQ `Select` after materializing complete objects, which has already paid the cost of creating those objects.

`WithOrderedProjection` keeps those savings and reduces the cost of finding selected fields. With ordinary projection, the scanner computes a name fingerprint and looks up which selected column it matches. Ordered projection compares against only the next expected name, then advances to the following selected column. It avoids the general name-lookup table and fingerprint computation. Unselected properties can still occur between matches: the `debug` property above is allowed between `event` and `timestamp`.

```csharp
var events = new NdjsonReaderBuilder()
    .WithOrderedProjection(p => p.Property("event").Property("timestamp"))
    .Build();
```

Both examples expose columns in the order `event`, `timestamp`. If the input instead puts `timestamp` before `event`, ordinary projection succeeds; ordered projection rejects the record because its order contract is broken. Use ordered projection only when the producer guarantees that selected properties retain their relative order. It does not require every property or nested structure to remain identical; that is a separate stable-object-shape contract.

Both projection modes read the rest of the record and reject invalid unselected JSON. After finding the last selected field, the scanner stops name lookup but continues syntax validation to the record boundary. Malformed data in a later `tags` value still fails. On the combined framing/projection paths described below, one scan also locates that boundary, avoiding a separate framing pass.

The benefit is usually most noticeable when selecting a small subset of a wide object, especially when omitted properties contain nested objects or arrays. If nearly every field is selected, little metadata work is avoided. Ordered projection's additional benefit depends on the property names, their positions, and the input workload. Neither option reduces input bytes or guarantees a fixed speedup; measure representative input and include the fields your application actually reads.

### JSON projection

`JsonReaderBuilder.WithProjection` validates the complete JSON value while materializing only selected properties. The JSON reader combines projection with object framing, reuses field metadata, compares property names without creating a string for ordinary unescaped names, and stops projection lookup once all selected fields have been found.

`JsonReaderBuilder.WithOrderedProjection` is a faster opt-in contract. Selected properties must occur in the configured order, although unselected properties may occur between them. Reordered selected properties are rejected.

### NDJSON projection

`NdjsonReaderBuilder.WithProjection` exposes selected properties in configuration order and accepts those properties in any input order. For eligible configurations, the reader locates the record boundary and selected fields in one pass through the input. It validates the complete JSON value, skips unselected values, and populates a reusable projected-field buffer. Property-name fingerprints support lookup; lookup stops once all selected fields have been found, while validation continues.

This combined path is selected automatically for ordinary or ordered projection when all of the following hold:

- The line terminator is LF (`"\n"`) or CRLF (`"\r\n"`).
- Comments are disabled.
- The configured whitespace characters are exactly space, tab, carriage return, and line feed.
- Neither `WithRecordPath` nor `WithStableObjectShape` is enabled.

`NdjsonReaderBuilder.WithOrderedProjection` avoids the fingerprint lookup by comparing each property with the next expected selected name. Unselected properties may occur between selected properties; reordered selected properties are rejected.

Other configurations retain the compatible path: frame the complete record first, then parse or project its JSON values. Custom terminators, comments, record-path selection, and stable-shape parsing therefore must not be described as using combined framing and projection. Both paths preserve format validation, synchronous/asynchronous reads, and records spanning buffer boundaries.

### NDJSON stable object shape

`NdjsonReaderBuilder.WithStableObjectShape` is an opt-in trusted-input contract for streams whose objects always have the same recursive JSON structure and property order. The first object establishes object property counts, array lengths, labels, and ordinals at every level. Later objects validate property-name syntax but skip decoding, comparison, and lookup, then write values directly by ordinal into reusable field-array trees. Property-count, array-length, and scalar/container drift throws before a record is exposed. Same-count renames and reorders are deliberately not detected, so callers must use this option only for trusted stable-shape input. Scalar leaf types remain dynamic unless a schema or typed accessor establishes and caches a conversion for an ordinal.

### LTSV and logfmt

LTSV and logfmt use reusable chunk buffers for line framing and parse fields directly from the captured character memory. They reuse scratch field storage and published field metadata as records advance, rather than creating a complete line string for every row. Records crossing chunk boundaries are consolidated in pooled storage; decoded logfmt escapes and materialized output strings can still require allocations.

Both formats also benefit from shared row-shape caching. Their labels, field order, and field count may change on each row; they do not use JSON projection or the trusted stable-object-shape contract. Synchronous and asynchronous reads share the framing and parsing implementation. Borrowed record memory and metadata are valid only until the next read or reader disposal; materialized outputs retain their own values.

See [Key-value formats](../key-value-formats/) for logfmt and LTSV examples, and [JSON and NDJSON](../json-and-ndjson/) for JSON-specific configuration.
