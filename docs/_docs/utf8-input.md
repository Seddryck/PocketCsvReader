---
title: Choose native UTF-8 CSV input
tags: [performance]
---

## Pooling repeated field values

Set `PoolUtf8Strings = true` on `ParserOptimizationOptions` to reuse strings for repeated fields read from native UTF-8 input. The cache checks the column's most recently pooled value before hashing. Cache hits return the existing string before decoding. Each reader owns its bounded cache, and returned strings remain valid after the reader advances or is disposed.

The reader uses its regular field handling for escaped fields that need unescaping, configured schemas or resource sequences, and decoded-character input. An explicit `PoolString` callback takes precedence.

## Complete native UTF-8 example

The syntax here is ASCII and the input is known UTF-8. The returned customer name owns its decoded string.

<!-- example: utf8-input -->
```csharp
using System.Text;
using PocketCsvReader;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithStreamInitialization(StreamInitializationOptions.Utf8)
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read()) Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
```

<!-- output: utf8-input -->
```text
1: Ada
```

Character parsing remains the default. To scan UTF-8 bytes directly, configure the same reader API:

```csharp
var csvReader = new CsvReaderBuilder()
    .WithStreamInitialization(StreamInitializationOptions.Utf8)
    .Build();

// Or probe the input and select a compatible parser automatically:
var profile = new CsvProfile(new DialectDescriptor())
{
    StreamInitialization = new(ProbeEncoding: true, InputMode: CsvInputMode.Auto)
};
```

| Input mode | Resolved UTF-8 encoding | Other encoding |
| --- | --- | --- |
| `DecodedChars` (default) | Character parser | Character parser |
| `Auto` | Byte parser for supported profiles | Character parser |
| `Utf8Bytes` | Byte parser for supported profiles | `NotSupportedException` |

An explicit resource encoding takes precedence over BOM detection. Otherwise, probing recognizes UTF-8, UTF-16, and UTF-32 BOMs, including on non-seekable streams; input without a BOM defaults to UTF-8. The resolved encoding's matching BOM is consumed. `StreamInitializationOptions.Utf8` disables encoding probing for a known UTF-8 source, while still consuming a UTF-8 BOM. When probing is disabled, other BOMs do not change the encoding. Invalid UTF-8 uses the same replacement fallback as the default UTF-8 `StreamReader`; skipped fields need no decoding or validation.

The byte parser supports ASCII delimiters, quotes, escapes, comments, and record terminators, with arbitrary UTF-8 field text. It handles quoted multiline records, escaped quotes, short reads, and fields larger than the configured buffer. Array dialects, non-ASCII syntax characters, and bad-data policies use the character parser in `Auto` and throw `NotSupportedException` in `Utf8Bytes`. Selection happens after decompression. Both synchronous and asynchronous readers honor these policies.

Records remain in pooled byte buffers, with ordinary text scanned in bulk between CSV syntax bytes. Eligible ASCII string getters decode directly into the returned string. Other field access uses a reusable pooled character cache when sanitization, custom parsing, or multibyte text requires it, avoiding intermediate per-field strings. Eligible typed getters can parse field bytes directly. See the value parser configuration below. Character-based object mappers materialize a complete character record with matching character offsets. Parser-owned record memory is valid only until the next read or disposal; returned strings and mapped objects remain valid.

## UTF-8 value parsers

Native UTF-8 input automatically uses byte parsers for eligible `short`, `int`, `long`, `bool`, and invariant `decimal` reads. It also handles `DateOnly` in `yyyy-MM-dd` form and `TimeOnly` in 24-hour `HH:mm:ss` form. For example, `GetTime(0)` parses `23:59:59` directly. The character parser handles other supported forms, such as fractional seconds.

Opt in to reading ISO dates as `DateTime` with `UseIsoDateAsDateTime()`:

<!-- example: utf8-date-as-datetime -->
```csharp
using System.Globalization;
using PocketCsvReader;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithoutHeader())
    .WithStreamInitialization(StreamInitializationOptions.Utf8)
    .WithParsers(p => p.UseIsoDateAsDateTime())
    .Build();

using var stream = new MemoryStream("2024-02-29,23:59:59"u8.ToArray());
using var reader = csv.ToDataReader(stream);
if (reader.Read())
{
    var date = reader.GetDateTime(0);
    var time = reader.GetTime(1);
    Console.WriteLine(date.ToString("O", CultureInfo.InvariantCulture));
    Console.WriteLine(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
}
```

<!-- output: utf8-date-as-datetime -->
```text
2024-02-29T00:00:00.0000000
23:59:59
```

The opt-in returns midnight with `DateTimeKind.Unspecified` for ISO dates. Full timestamps and other supported inputs use regular `DateTime` parsing. Explicit runtime parsers and schema field parsers or formats take precedence.

See [runtime parsers](../fluent-api-runtime-parsers/#utf-8-parsers) to pair your own byte parser with a character fallback.

## Performance

Byte parsing is opt-in because throughput and allocations depend on the workload, especially how many strings are materialized. The `Utf8Input` benchmarks compare ASCII and Unicode input, narrow and wide records, multiline fields, numeric getters, and skipped columns. See the [measured comparison](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/Utf8Input.md). Run them with:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*Utf8Input*'
```



See [initialization](../fluent-api-resource/) and [stream ownership](../stream-lifetimes/). This byte parser is a CSV feature, not an input mode shared by all formats.
