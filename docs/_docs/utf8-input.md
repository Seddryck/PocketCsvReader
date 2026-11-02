---
title: Choose native UTF-8 CSV input
tags: [performance]
---

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

Records remain in pooled byte buffers, with ordinary text scanned in bulk between CSV syntax bytes. Eligible ASCII string getters decode directly into the returned string. Other field access uses a reusable pooled character cache when sanitization, custom parsing, or multibyte text requires it, avoiding intermediate per-field strings. Compatible integer and Boolean getters can parse bytes directly when no custom parsers or sequence substitutions apply. Character-based object mappers materialize a complete character record with matching character offsets. Parser-owned record memory is valid only until the next read or disposal; returned strings and mapped objects remain valid.

Byte parsing is opt-in because throughput and allocations depend on the workload, especially how many strings are materialized. The `Utf8Input` benchmarks compare ASCII and Unicode input, narrow and wide records, multiline fields, numeric getters, and skipped columns. See the [measured comparison](https://github.com/Seddryck/PocketCsvReader/blob/main/PocketCsvReader.Benchmark/Utf8Input.md). Run them with:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*Utf8Input*'
```



See [initialization](../fluent-api-resource/) and [stream ownership](../stream-lifetimes/). This byte parser is a CSV feature, not an input mode shared by all formats.
