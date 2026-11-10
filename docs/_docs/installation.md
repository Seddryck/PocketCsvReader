---
title: Installation and package selection
tags: [installation]
---

The projects target **.NET 8, .NET 9, and .NET 10**. Use a matching .NET SDK and select the package for the input or integration you need. Extension packages reference the core library.

## Install with the .NET CLI

Run from the directory containing your application project:

```sh
dotnet add package PocketCsvReader
```

For JSON input, for example:

```sh
dotnet add package PocketCsvReader.Json
```

Omitting `--version` selects the latest available package. To pin a release, append `--version` followed by that release's actual version number.

## Choose packages

| Package | Purpose | Guide |
| --- | --- | --- |
| `PocketCsvReader` | CSV/TSV and shared reading APIs | [First CSV](../basic-usage/) |
| `PocketCsvReader.Json` | JSON and JSONC | [JSON](../json-and-ndjson/) |
| `PocketCsvReader.Ndjson` | Newline-delimited JSON | [NDJSON](../ndjson/) |
| `PocketCsvReader.FixedWidth` | Fixed character positions | [Fixed-width](../fixed-width/) |
| `PocketCsvReader.KeyValue` | LTSV and logfmt | [Key-value](../key-value-formats/) |
| `PocketCsvReader.WebLogs` | Common, Combined, W3C | [Web logs](../web-log-formats/) |
| `PocketCsvReader.SecurityLogs` | CEF and LEEF | [Security logs](../security-log-formats/) |
| `PocketCsvReader.Arrow.ColumnarJson` | ClickHouse columnar exports and Arrow Integration JSON | [Columnar JSON](../columnar-json/) |
| `PocketCsvReader.Arrow.NetCdf` | Textual NetCDF datasets (.cdl) | [NetCDF CDL](../netcdf-cdl/) |
| `PocketCsvReader.Arrow` | Apache Arrow output batches | [Arrow](../apache-arrow/) |
| `PocketCsvReader.Chrononuensis` | Temporal period parsers | [Chrononuensis](../chrononuensis/) |

## Visual Studio Package Manager Console

```powershell
Install-Package PocketCsvReader
```

## NuGet CLI

```sh
nuget install PocketCsvReader
```

`nuget install` downloads packages into a directory; it does not add a modern project's package reference. Prefer `dotnet add package` or Visual Studio for that task.

Continue with [Read your first CSV](../basic-usage/) or the guide for your format.
