# PocketCsvReader

PocketCsvReader reads CSV and other structured text through forward-only data readers and streaming row outputs. Optional packages support JSON/JSONC, NDJSON, fixed-width, LTSV, logfmt, web logs, and security logs. The projects target .NET 8, 9, and 10.

[Documentation](https://seddryck.github.io/PocketCsvReader/docs/why-pocketcsvreader/) · [Installation](https://seddryck.github.io/PocketCsvReader/docs/installation/) · [Contributing](contributing.md)

## Packages

| Package | Purpose |
| --- | --- |
| `PocketCsvReader` | CSV/TSV and shared APIs |
| `PocketCsvReader.Json` | JSON and JSONC |
| `PocketCsvReader.Ndjson` | NDJSON |
| `PocketCsvReader.FixedWidth` | Fixed character positions |
| `PocketCsvReader.KeyValue` | LTSV and logfmt |
| `PocketCsvReader.WebLogs` | Common, Combined, W3C |
| `PocketCsvReader.SecurityLogs` | CEF and LEEF |
| `PocketCsvReader.Arrow` | Apache Arrow output |
| `PocketCsvReader.Chrononuensis` | Temporal period parsers |

## First example

```sh
dotnet add package PocketCsvReader
```

```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
// 1: Ada
```

See [output models](https://seddryck.github.io/PocketCsvReader/docs/output-models/), [reader filtering](https://seddryck.github.io/PocketCsvReader/docs/reader-filtering/), [object mapping](https://seddryck.github.io/PocketCsvReader/docs/mapper-object-builder/), [async reading](https://seddryck.github.io/PocketCsvReader/docs/async-reading/), [inference](https://seddryck.github.io/PocketCsvReader/docs/schema-inference/), and [performance guidance](https://seddryck.github.io/PocketCsvReader/docs/parser-optimizations/) for complete behavior and limitations.

Shared ownership, null, conversion, and performance contracts have their authoritative home in the documentation rather than duplicated README sections.
