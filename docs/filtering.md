---
title: Reader filtering
---

# Reader filtering

Configure conditions before building a reader:

```csharp
var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader())
    .WithFilter("Status", "Active")
    .WithFilter("Country", value => value.SequenceEqual("BE".AsSpan()))
    .WithFilter<int>("Age", age => age >= 18)
    .WithRowFilter(row =>
        row.GetFieldValue<int>("Quantity") > row.GetFieldValue<int>("Available"))
    .Build();
```

Each cell overload accepts a name or a zero-based ordinal:

- `WithFilter(column, expected, comparison = StringComparison.Ordinal)`
- `WithFilter(column, ParseSpan<bool> predicate)`
- `WithFilter<T>(column, Func<T, bool> predicate)`

An untyped lambda selects the span overload; specify `<T>` for parsed values.
Filters run in registration order with AND semantics and short-circuiting.
A true result keeps a row. `Read()` and `ReadAsync()` automatically skip rejected
rows before object construction or row materialization. Async input remains
asynchronous and checks cancellation between rejected rows; predicates run
synchronously. Cancellation cannot interrupt a predicate while it is running.

Filtering is inherited by all reader builders (CSV, JSON/JSONC, NDJSON, key-value,
fixed-width, web logs, and security logs), and applies to batches, objects, string/object
arrays, dictionaries, dynamic records, and DataTables. Existing readers with
no filters keep their existing paths. Profiles also expose `Filters` for
passing an existing builder configuration to a profile.

## Values, lifetimes, and errors

Span predicates receive sanitized, decoded logical values: quoted and unquoted
equivalent CSV cells compare equally. No intermediate string is required by
the span filter; decoding/escaping may use existing reusable buffers. Spans
are valid only during the predicate call.

Null cells never match equality or span filters. Non-nullable value-type
filters reject null without invoking the predicate. Nullable and reference-type
filters receive null and can decide explicitly. Empty text is a non-null span.

`WithRowFilter(Func<IRowView, bool>)` supplies a read-only borrowed view with
`FieldCount`, `GetName`, `GetOrdinal`, `IsDBNull`, `GetValue`, and
`GetFieldValue<T>` getters. Value/null getters accept names and ordinals.
Typed getters also accept explicit `ParseSpan<T>` parsers. The view cannot
advance or dispose the reader; use it only inside the callback. Ordinary C#
Boolean expressions support multi-field conditions:

```csharp
.WithRowFilter(row => !row.IsDBNull("Amount") &&
    row.GetFieldValue<decimal>("Amount") > row.GetFieldValue<decimal>("Limit"))
.WithFilter("Age", value => int.TryParse(value, out var age) && age >= 18)
```

Unknown names, invalid ordinals, invalid configuration, conversion failures,
and predicate exceptions propagate. They do not silently reject a row. Use an
explicit TryParse predicate when malformed text should be rejected.
Conditions after a false condition are not evaluated.

Typed filtering uses the same schema, formats, cultures, registered-parser
precedence, and conversions as typed getters. Named lookups are cached for a
stable schema and rebound when record labels change or a new batch file opens.
Filters can only refer to fields exposed by a configured format projection.

## Parsed-value reuse and source numbering

With filtering enabled, a lazy per-current-row cache shares successful typed
conversions across cell filters, row filters, subsequent getters, and default
CSV constructor projection. Cache keys include ordinal, result type, and the
effective parser delegate; explicit span-parser overrides share only with the
same parser. Different types or parsers remain independent. The cache clears
before every row advance, including rejected rows, EOF, and batch transitions.
Unrelated fields are not parsed eagerly. A span comparison performs no typed
conversion and does not allocate this cache.

The dictionary and boxed value-type results have allocation costs. Default
constructor projection uses getters when filtering is enabled; a custom
`SpanMapper<T>` controls its own raw parsing and cannot share getter conversions.
String projections still request strings even when a filter requested a number.

Source-position diagnostics and `RowCount` retain their source meaning.
CSV `WithRowNumber()` counts source data records before filtering, so rejected
rows leave gaps. Numbering continues across batch files. With a generated
column, ordinal zero is the generated number and source columns shift by one.
The row predicate can read/filter that number.

Run the focused performance check with:

```powershell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --reader-filtering-matrix
```

It compares no filtering, span filtering, a string row predicate, manual typed
filtering/projection, and configured typed filtering/projection. It reports
allocation per source row, elapsed time, and parser calls, and asserts that
configured typed filtering plus projection parses each field only once.

A short local .NET 10 Release check on 50,000 rows measured 0.22 bytes/row
without filtering, 0.24 with span filtering, 32.24 with a string row predicate,
and 24.26 with configured typed filtering/projection. The typed case made
50,000 parser calls versus 100,000 for manual filtering followed by projection.
The configured case took 56.52 ms versus 37.84 ms manually in this run: dictionary
lookup and boxing can outweigh reuse for cheap integer parsing. These are
single-process smoke measurements, not statistically controlled benchmarks.
