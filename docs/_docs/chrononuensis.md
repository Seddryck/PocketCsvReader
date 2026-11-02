---
title: Chrononuensis temporal types
tags: [configuration, parsing]
---

Install the optional `PocketCsvReader.Chrononuensis` NuGet package to parse temporal periods through Chrononuensis 0.28.0's public span APIs. The core `PocketCsvReader` package does not depend on Chrononuensis. Both packages support .NET 8, 9, and 10.

```shell
dotnet add package PocketCsvReader.Chrononuensis
```

```csharp
using Chrononuensis;
using PocketCsvReader;
using PocketCsvReader.Configuration;
using PocketCsvReader.Chrononuensis;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithoutHeader())
    .WithSchema(s => s.Indexed().WithField<YearMonth>())
    .WithParsers(options => options.UseChrononuensis())
    .Build();

using var stream = new MemoryStream("2026-10"u8.ToArray());
using var reader = csv.ToDataReader(stream);
reader.Read();
YearMonth month = reader.GetFieldValue<YearMonth>(0);
```

### Supported types and default patterns

`UseChrononuensis()` registers exactly these ten types. Other types, including `DateOnly`, `DateTime`, numeric types, and user-defined types, keep their existing behavior. No selective registration is required.

| Type | Chrononuensis pattern | Example |
|---|---|---|
| `MonthDay` | `MM-dd` | `10-22` |
| `YearDay` | `yyyy-jjj` | `2026-295` |
| `YearWeek` | `yyyy-'W'ww` | `2026-W43` |
| `YearMonth` | `yyyy-MM` | `2026-10` |
| `YearQuarter` | `yyyy-'Q'q` | `2026-Q4` |
| `YearSemester` | `yyyy-'H'S` | `2026-H2` |
| `Year` | `yyyy` | `2026` |
| `Olympiad` | `{o:RN} {#Olympiad}` | `I Olympiad` |
| `Decade` | `tttt's'` | `2020s` |
| `Century` | `c` | `21` |

### Formats, cultures, and precedence

Use schema field formats for nondefault representations:

```csharp
var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithoutHeader())
    .WithSchema(s => s.Indexed()
        .WithCustomField(typeof(YearQuarter), f => f.WithFormat("'Q'q.yy",
            System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))))
    .WithParsers(options => options.UseChrononuensis())
    .Build();
// Parses Q4.26 as YearQuarter(2026, 4).
```

Custom and temporal field patterns and their culture providers are passed to Chrononuensis's public span `Parse` overloads. Fields without a format use Chrononuensis's default pattern and the provider supplied to `UseChrononuensis(provider)`, or invariant culture when omitted. Field descriptors supply their own culture (invariant by default), taking precedence over the integration provider. Pattern tokens, localized labels, validation, and how providers are interpreted follow Chrononuensis, rather than .NET `DateTime` formatting rules. The integration does not translate tokens or localize text independently.

Explicit field parsers take precedence over field formats and integration defaults. Explicit runtime `WithParser` registrations override integration defaults whether called before or after `UseChrononuensis()`. Existing schema format precedence over runtime type parsers is preserved. Repeated `UseChrononuensis()` calls are safe; the latest integration provider applies to defaults without replacing explicit registrations.

```csharp
var parsers = new RuntimeParsersDescriptorBuilder()
    .WithParser((ReadOnlySpan<char> input) => new YearMonth(2000, 1))
    .UseChrononuensis(); // Keeps the explicit parser above.
```

### Errors and parsing paths

Invalid values retain the exceptions raised by Chrononuensis's public `Parse` methods, including `FormatException` for malformed or out-of-range input. They are not replaced with default temporal values. Existing null and sequence handling still runs before parsing: nulls in non-nullable fields throw `InvalidCastException`. Typed reads, boxed `GetValue` reads, and array elements share the registered parsers.

PocketCsvReader has no source-generated reader path in this repository. The runtime tests exercise all ten types, including the types whose public implementations are generated inside the published Chrononuensis package. Consumers do not need a source generator or access to upstream internal parsers.

### Performance validation

The adapter passes `ReadOnlySpan<char>` directly to Chrononuensis, including fields with explicit formats. This avoids intermediate input strings and reflection calls; Chrononuensis itself still allocates during parsing. Allocation regression tests compare integration reads with direct public span parsing after warmup.

`PocketCsvReader.Benchmark/ChrononuensisParsing.cs` compares the existing formatted reflection path, a string custom parser, the integration, and direct upstream span parsing. It also compares integer reads with integration disabled and enabled. Registration and initial parser caching are outside the measured operations.

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*ChrononuensisParsing*'
```

Detailed machine-specific measurements belong with the [benchmark methodology](../buffering-and-benchmarks/#benchmarks). The integration avoids intermediate input strings; the upstream parser can still allocate. No fixed throughput or zero-allocation guarantee is implied.
