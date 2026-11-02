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
