---
title: Parser optimizations
tags: [configuration]
---
The `ParserOptimizationOptions` record provides a set of parameters to fine-tune the performance and behavior of the CSV parser. These options allow developers to balance flexibility, accuracy, and speed depending on the specific requirements of their use case.

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

| Optimization | JSON | NDJSON | LTSV/logfmt |
| --- | --- | --- | --- |
| Cache repeated label sequences and ordinal dictionaries | Yes | Yes | Yes |
| Keep synchronous current-record memory without a per-row wrapper | Yes | Yes | Yes |
| Read records through reusable chunk buffers | Yes | Yes | No; records currently use line-based strings |
| Project selected JSON properties | `WithProjection` | `WithProjection` | Not applicable |
| Reuse the projected `FieldSpan` buffer between records | Yes | Yes | Not applicable |
| Find projected fields while locating the record boundary | Yes | No; projection runs after NDJSON framing | Not applicable |
| Stop property lookup after every projected field is found | Yes | No | Not applicable |
| Require projected properties in a known order | `WithOrderedProjection` | No | Not applicable |

### Repeated labeled row shapes

When rows repeat the same labels in the same order, their label strings and name-to-ordinal dictionary are reused. A different field count or label order is treated as another row shape. Up to 64 shapes are cached per data reader so highly heterogeneous input remains bounded.

### JSON projection

`JsonReaderBuilder.WithProjection` validates the complete JSON value while materializing only selected properties. The JSON reader combines projection with object framing, reuses field metadata, compares property names without creating a string for ordinary unescaped names, and stops projection lookup once all selected fields have been found.

`JsonReaderBuilder.WithOrderedProjection` is a faster opt-in contract. Selected properties must occur in the configured order, although unselected properties may occur between them. Reordered selected properties are rejected.

### NDJSON projection

`NdjsonReaderBuilder.WithProjection` exposes selected properties in configuration order and accepts those properties in any input order. NDJSON first finds the complete record according to its line terminator and comment settings, then uses the shared JSON field parser to validate the record, skip unselected values, and populate a reusable projected-field buffer.

Because NDJSON framing and property projection are separate passes, it does not receive the JSON reader's projection-during-framing or ordered-projection fast paths.

### LTSV and logfmt

The key-value readers benefit from shared row-shape caching and direct synchronous record storage. They do not use JSON projection or JSON framing. Their current record sources read one complete line as a string before parsing its labeled fields.
