---
title: Tune reader options
tags: [configuration]
---
The `ParserOptimizationOptions` record provides settings shared by several readers. The defaults and descriptions below refer to CSV unless stated otherwise. Other formats use their own framing and syntax rules; accepting the same options record does not mean every CSV setting changes their behavior. See [JSON, NDJSON, and key-value readers](#json-ndjson-and-key-value-readers) for their defaults and format-specific optimizations.

## Options and Their Descriptions

These are the shared record constructor defaults. Format builders may replace them; a setting stored in an options record is not evidence that every parser uses it.

### `NoTextQualifier`
Default `false`. Legacy CSV optimization flag; actual quote syntax is configured by the dialect's `QuoteChar`. Do not use this flag to disable JSON/logfmt grammar.
### `UnescapeChars`
Default `true`. Controls applicable CSV escape sanitization. Native JSON and logfmt escape decoding follows those parsers and is not disabled by this flag.
### `HandleSpecialValues`
Default `true`. Controls sequence sanitization where implemented; see [value handling](../sequences/). It does not turn off native JSON null recognition.
### `RowCountAtStart`
Default `false`. CSV can pre-count records before reading; this may require seeking and an extra pass. It is inappropriate for forward-only streaming formats.
### `ExtendIncompleteRecords`
Default `true`. CSV getter handling for absent trailing fields; it is not a universal materializer padding rule and setting false is not a configurable skip-record policy. See [missing values](../sequences/) and [recovery](../validation-and-errors/).
### `ReadAhead`
Default `true`. CSV selects its double-buffer read-ahead path. Other formats can ignore or normalize the flag. Buffer size and read-ahead are separate choices.
### `BufferSize`
Default `4096` characters in the shared record. The NDJSON/key-value builders use 64 KB defaults. Positive sizes are required by those profiles. A larger chunk can reduce refills; it does not cap record length.
### `PoolString`
Default `null`. Optional string-pooling delegate used by the value mapper. Pooling can reduce repeated strings but retained pools have a memory cost.
### `LookupTableChar`
Default `true`. Legacy CSV flag; do not infer that switching it changes every current scanner. Delimiter/quote settings belong to the dialect, and JSON/key-value scanners use their own implementation.

### `TrackPosition`
Default `false`. Prefer `.WithPositionTracking()` before reading. See [source positions](../source-positions/) for units and overhead.

## Which options apply?

| Reader | Input buffer | ReadAhead / RowCountAtStart | Syntax and field handling |
| --- | --- | --- | --- |
| CSV | Shared 4 KB default; configurable | CSV-specific paths | Dialect plus applicable sanitizer/incomplete-field settings |
| JSON | Format's buffered source | Not CSV double-buffer/count selection | Native JSON grammar and decoding |
| NDJSON | Builder defaults to 64 KB | Not used to select its record source | Native JSON plus NDJSON framing |
| LTSV/logfmt | Builder defaults to 64 KB | Forced false | Native key-value grammar/decoding |
| Fixed-width | Configurable character buffer | No universal CSV semantics; row count disabled by builder | Field offsets, widths, padding |
| Web/security logs | Format-specific chunk source | Do not assume CSV semantics | Format grammar and applicable value sanitization |

Use native [UTF-8 input](../utf8-input/) independently from these options. For measuring changes see [benchmarks](../buffering-and-benchmarks/#benchmarks).

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

## Automatic storage and projection optimizations

See [buffering and benchmark guidance](../buffering-and-benchmarks/) for memory reuse and [projection and stable shapes](../json-projection/) for JSON/NDJSON fast-path contracts.

## Why projection improves performance

The worked comparison moved to [Why projection improves performance](../json-projection/#why-projection-improves-performance).

### Repeated labeled row shapes
See [row-shape caching](../buffering-and-benchmarks/#labeled-row-shapes).
### JSON projection
See [JSON projection](../json-projection/#json-projection).
### NDJSON projection
See [NDJSON projection](../json-projection/#ndjson-projection).
### Logfmt and LTSV column projection
Both builders support selecting, renaming, and ordering output columns. See [Project a fixed set of columns](../key-value-formats/#project-a-fixed-set-of-columns).

### Stable field layout
`WithStableFieldLayout()` is available for JSON, NDJSON, logfmt, and LTSV. See the [JSON and NDJSON layout contract](../json-projection/#stable-field-layout) and the [logfmt and LTSV layout contract](../key-value-formats/#reuse-a-trusted-source-layout).
### LTSV and logfmt
See [key-value buffering](../buffering-and-benchmarks/#key-value-records).
