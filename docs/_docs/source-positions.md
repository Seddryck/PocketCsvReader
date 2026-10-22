---
title: Source positions
tags: [configuration]
---

Source position tracking is optional and disabled by default. Enable it before creating the data reader:

```csharp
var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\r\n"))
    .WithPositionTracking()
    .Build();

using var reader = csv.ToDataReader(File.OpenRead("input.csv"));
while (reader.Read())
{
    if (reader.CurrentPosition is { } position)
        Console.WriteLine($"Record {position.RecordNumber}, line {position.PhysicalLine}, " +
                          $"offset {position.Offset} ({position.OffsetUnit})");
}
```

All format builders support `WithPositionTracking()` and `WithPositionTracking(false)`. The explicit builder choice is retained when `WithParserOptimizations` is subsequently called. Direct profile configuration uses the existing parser options:

```csharp
var profile = new CsvProfile(new DialectDescriptor(Header: false))
{
    ParserOptimizations = new ParserOptimizationOptions { TrackPosition = true },
    StreamInitialization = StreamInitializationOptions.Utf8
};
```

For other profiles, pass these options through their `parserOptimizations` constructor argument. Configure tracking before reading; changing options on an initialized reader does not enable or disable its tracker.

`RecordPosition` is an immutable `readonly record struct`, containing `RecordNumber`, `PhysicalLine`, `Offset`, and `OffsetUnit`. Concrete data readers and the common `IAsyncDataReader` interface expose `RecordPosition? CurrentPosition`. `DbDataReaderAdapter` also exposes it; when using a value declared as `DbDataReader`, cast to the adapter or use its `InnerReader`. `HasRows` prefetch does not expose a current position until `Read` or `ReadAsync` returns that record.

`CurrentPosition` is null before the first successful read, after an unsuccessful read or close, and throughout reading when tracking is disabled. A final record without a terminator retains its position until the next unsuccessful read, even if the parser already knows it reached EOF. Snapshots can be saved and remain unchanged when the reader advances. Sync and async reads use the same counting rules.

Record numbers and physical lines are one-based; offsets are zero-based. Headers, comments, directives, and skipped blank lines affect physical lines and offsets but do not consume data-record numbers. Formats that return blank records count them as records. A malformed CSV data record that is skipped still consumes its logical number. Existing `RowCount` behavior is unchanged and can differ from `RecordNumber`.

A multiline CSV record reports its starting line and offset. CRLF counts as one physical line break, including when split across buffers; lone CR and LF also count as physical breaks, even with a custom record terminator. JSONPath-selected JSON/NDJSON rows report their selected token's start in the source and receive sequential logical record numbers.

| Active parser | OffsetUnit | Offset meaning |
| --- | --- | --- |
| Character parser, including UTF-8 decoded by StreamReader | Utf16CodeUnits | Number of decoded .NET char values before the record; supplementary characters occupy two units |
| Native UTF-8 CSV parser | Bytes | Number of UTF-8 input bytes before the record |

The initial BOM consumed by the reader is excluded from both origins. Later U+FEFF characters remain data. For compressed or transformed input, positions refer to the parser input after preprocessing, rather than the original compressed file. Byte offsets therefore refer to decompressed UTF-8 bytes in that case. Neither mode depends on stream seeking or `Stream.Position`.

The unit follows the parser actually selected. Tracking does not change parser eligibility: `Auto` falls back to characters for unsupported native UTF-8 profiles, and forced byte mode rejects them. In particular, a CSV `BadDataPolicy` currently requires character parsing. Native UTF-8 supports ASCII syntax without array fields or a bad-data policy. Knowing an encoding and a numeric character offset alone does not generally permit conversion to a byte offset.

With tracking enabled, `BadDataContext.RecordPosition` identifies the malformed record's start. `RecordNumber` identifies that same logical record; `PhysicalLine` and `Offset` identify the offending input, using the explicit `OffsetUnit`. With tracking disabled, `RecordPosition` is null and the existing diagnostic behavior remains: the legacy offset is record-relative and line/record values are approximate. There is no implicit activation for diagnostics.

Tracking adds no heap allocation per record. It uses an optional tracker per reader, native span-length accounting, and newline searches only when enabled. It preserves UTF-8 bulk payload scanning and does not decode skipped fields for metadata. See the [benchmark methodology and measurements](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/PositionTracking.md) for measured enabled and disabled overhead; zero overhead is not guaranteed.
