# PocketCsvReader

![Logo](https://raw.githubusercontent.com/Seddryck/PocketCsvReader/main/assets/PocketCsvReader-icon-256.png)

PocketCsvReader is a highly efficient and lightweight library tailored for parsing delimited flat files. Optional extension packages add support for formats such as fixed-width, NDJSON, LTSV, logfmt, Common Log Format, and W3C Extended Log Format. With a focus on simplicity and performance, it offers seamless file reading and supports versatile outputs, including DataTables, string arrays, strongly-typed object mapping and an IDataReader interface.

[About][] | [Install][] | [Quick-start][]

[About]: #about (About)
[Install]: #install (Install)
[Quick-start]: #quick-start (Quick-start)

## About

**Social media:** [![website](https://img.shields.io/badge/website-seddryck.github.io/PocketCsvReader-fe762d.svg)](https://seddryck.github.io/PocketCsvReader)
[![twitter badge](https://img.shields.io/badge/twitter%20PocketCsvReader-@Seddryck-blue.svg?style=flat&logo=twitter)](https://twitter.com/Seddryck)

**Releases:** [![GitHub releases](https://img.shields.io/github/v/release/seddryck/PocketCsvReader?label=GitHub%20releases)](https://github.com/seddryck/PocketCsvReader/releases/latest) 
[![nuget](https://img.shields.io/nuget/v/PocketCsvReader.svg)](https://www.nuget.org/packages/PocketCsvReader/) [![GitHub Release Date](https://img.shields.io/github/release-date/seddryck/PocketCsvReader.svg)](https://github.com/Seddryck/PocketCsvReader/releases/latest) [![licence badge](https://img.shields.io/badge/License-Apache%202.0-yellow.svg)](https://github.com/Seddryck/PocketCsvReader/blob/master/LICENSE) 

**Dev. activity:** [![GitHub last commit](https://img.shields.io/github/last-commit/Seddryck/PocketCsvReader.svg)](https://github.com/Seddryck/PocketCsvReader/commits)
![Still maintained](https://img.shields.io/maintenance/yes/2027.svg)
![GitHub commit activity](https://img.shields.io/github/commit-activity/y/Seddryck/PocketCsvReader)

**Continuous integration builds:** [![CI and release](https://github.com/Seddryck/PocketCsvReader/actions/workflows/ci-release.yml/badge.svg)](https://github.com/Seddryck/PocketCsvReader/actions/workflows/ci-release.yml)
[![CodeFactor](https://www.codefactor.io/repository/github/seddryck/PocketCsvReader/badge)](https://www.codefactor.io/repository/github/seddryck/PocketCsvReader)
[![codecov](https://codecov.io/github/Seddryck/PocketCsvReader/branch/main/graph/badge.svg?token=PCRL1Y6JVR)](https://codecov.io/github/Seddryck/PocketCsvReader)
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FSeddryck%2FPocketCsvReader.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2FSeddryck%2FPocketCsvReader?ref=badge_shield)

**Status:** [![stars badge](https://img.shields.io/github/stars/Seddryck/PocketCsvReader.svg)](https://github.com/Seddryck/PocketCsvReader/stargazers)
[![Bugs badge](https://img.shields.io/github/issues/Seddryck/PocketCsvReader/bug.svg?color=red&label=Bugs)](https://github.com/Seddryck/PocketCsvReader/issues?utf8=%E2%9C%93&q=is:issue+is:open+label:bug+)
[![Top language](https://img.shields.io/github/languages/top/seddryck/PocketCsvReader.svg)](https://github.com/Seddryck/PocketCsvReader/search?l=C%23)

## Install

Replace `<VersionNumber>` with the desired version in each of the following solutions. If no version is specified, the latest version will be installed.

### NuGet CLI

1. Open a command prompt or terminal.
2. Run the following command:

   ```bash
   nuget install PocketCsvReader -Version <VersionNumber>
   ```
   
## Visual Studio Package Manager Console

1. Open the **Package Manager Console** from **Tools > NuGet Package Manager > Package Manager Console**.
2. Run the following command:

   ```powershell
   Install-Package PocketCsvReader -Version <VersionNumber>
   ```
## Dotnet-CLI

1. Open a terminal or command prompt.
2. Navigate to the directory of your project.
3. Run the following command:

   ```bash
   dotnet add package PocketCsvReader --version <VersionNumber>
   ```

   For fixed-width files, install the extension package as well:

   ```bash
   dotnet add package PocketCsvReader.FixedWidth --version <VersionNumber>
   ```

   For LTSV and logfmt records, install the key-value extension package:

   ```bash
   dotnet add package PocketCsvReader.KeyValue --version <VersionNumber>
   ```

   For Common Log Format and W3C Extended Log Format files, install the web-log extension package:

   ```bash
   dotnet add package PocketCsvReader.WebLogs --version <VersionNumber>
   ```
## Quick-start

The `CsvReader` class is a flexible and efficient tool for reading and parsing CSV files or streams into various formats, such as `DataTable`, `IDataReader`, or strongly-typed objects. This documentation explains the basics of how to use the class, including common use cases and examples.

### Features

- Read CSV files or streams into a `DataTable`.
- Access CSV data in a forward-only, read-only manner using `IDataReader`.
- Map CSV records to strongly-typed objects.
- Map CSV records to array of strings.
- Customizable CSV parsing profiles for delimiters, quote handling, and more.
- Supports encoding detection through the `IEncodingDetector` interface.
- Read fixed-width files in one forward-only pass with field conversion deferred until a `Get*` accessor requests that field.
- Read labeled LTSV and logfmt records through one shared reader and format-specific builders.
- Read Common Log Format and W3C Extended Log Format files with format-aware streaming readers.

Fixed-width support is distributed separately as `PocketCsvReader.FixedWidth`.

LTSV and logfmt support is distributed separately as `PocketCsvReader.KeyValue`:

```csharp
using PocketCsvReader.KeyValue.Configuration;

var ltsvReader = new LtsvReaderBuilder().Build();
var logfmtReader = new LogfmtReaderBuilder().Build();

using var dataReader = logfmtReader.ToDataReader("application.log");
while (dataReader.Read())
{
    var level = dataReader.GetString(dataReader.GetOrdinal("level"));
    var message = dataReader.GetString(dataReader.GetOrdinal("message"));
}
```

Common and W3C Extended web-log support is distributed separately as `PocketCsvReader.WebLogs`:

```csharp
using PocketCsvReader.WebLogs;

using var common = new CommonLogReader().ToDataReader("access.log");
while (common.Read())
{
    var request = common.GetString(common.GetOrdinal("Request"));
    var status = common.GetInt32(common.GetOrdinal("StatusCode"));
}

using var w3c = new W3cExtendedLogReader().ToDataReader("u_ex.log");
while (w3c.Read())
{
    var method = w3c.GetString(w3c.GetOrdinal("cs-method"));
    var path = w3c.GetString(w3c.GetOrdinal("cs-uri-stem"));
}
```

### Initialization

You can create an instance of `CsvReader` with various configurations:

```csharp
// Default configuration: comma-delimited, double quotes for escaping, 4 KB buffer size.
var csvReader = new CsvReader();

// Custom CSV profile (e.g., semicolon-delimited, double quotes for escaping).
var csvReaderWithProfile = new CsvReader(CsvProfile.SemiColumnDoubleQuote);

// Custom buffer size for large files.
var csvReaderWithBuffer = new CsvReader(bufferSize: 64 * 1024);

// Both custom profile and buffer size.
var csvReaderCustom = new CsvReader(CsvProfile.SemiColumnDoubleQuote, bufferSize: 16 * 1024);
```

### Reading CSV Data

#### Reading Into a `DataTable`

The `ToDataTable` method reads CSV data and returns a `DataTable` containing all rows and fields.

```csharp
DataTable dataTable = csvReader.ToDataTable("example.csv");
```

or to read from a stream,

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
DataTable dataTable = csvReader.ToDataTable(stream);
```

### Accessing Data with `IDataReader`

The `ToDataReader` method provides a forward-only, read-only `CsvDataReader` implementing `IDataReader` for processing large files efficiently. `IDataReader` itself is synchronous; PocketCsvReader readers also implement `IAsyncDataReader` when asynchronous stream I/O is needed.

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
using var reader = csvReader.ToDataReader(stream);
while (reader.Read())
{
    Console.WriteLine(reader[0]); // Access the first column of the current row.
    Console.WriteLine(reader.GetDateTime(1)); // Access the second column of the current row as an object boxing a DateTime.
    Console.WriteLine(reader.GetFieldValue<DateOnly>(2)); // Access the third column of the current row as DateOnly.
}
```

For consumers built around the standard `DbDataReader` abstraction, use `ToDbDataReader`. It is supported by every PocketCsvReader format and by CSV batch readers. Asynchronous advancement uses the underlying stream's asynchronous operations and accepts a cancellation token. Field getters remain synchronous because the current record is already buffered after `ReadAsync` completes.

```csharp
await using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
await using DbDataReader reader = csvReader.ToDbDataReader(stream);
while (await reader.ReadAsync(cancellationToken))
{
    Console.WriteLine(reader.GetInt32(0));
    Console.WriteLine(reader.GetString(1));
}
```

### Reading as Arrays

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
foreach (var record in csvReader.ToArrayString(stream))
{
    Console.WriteLine(string.Join(", ", record));
}
```

### Mapping Records to Strongly-Typed Objects

The `To<T>` method maps CSV records to objects of a specified type.

```csharp
public class Person
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
}

using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
IEnumerable<Person> people = csvReader.To<Person>(stream);

foreach (var person in people)
{
    Console.WriteLine($"{person.FirstName} {person.LastName}, Age: {person.Age}");
}
```

