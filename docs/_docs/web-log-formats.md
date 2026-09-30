---
title: Web log formats
subtitle: Read Common Log Format and W3C Extended Log Format files
tags: [usage]
---

Install the `PocketCsvReader.WebLogs` extension to parse Common Log Format (CLF) and W3C Extended Log Format files.

## Common Log Format

`CommonLogReader` exposes the standard seven CLF fields as `RemoteHost`, `Identity`, `AuthenticatedUser`, `Timestamp`, `Request`, `StatusCode`, and `ResponseBytes`.

```csharp
using PocketCsvReader.WebLogs;

using var reader = new CommonLogReader().ToDataReader("access.log");
while (reader.Read())
{
    var host = reader.GetString(reader.GetOrdinal("RemoteHost"));
    var request = reader.GetString(reader.GetOrdinal("Request"));
    var status = reader.GetInt32(reader.GetOrdinal("StatusCode"));
}
```

The parser recognizes bracketed timestamps and quoted request lines. A hyphen (`-`) is returned as a null value.

## W3C Extended Log Format

`W3cExtendedLogReader` discovers column names from the required `#Fields:` directive and retains directives such as `#Software`, `#Version`, and `#Date` as metadata.

```csharp
using PocketCsvReader.WebLogs;

using var reader = new W3cExtendedLogReader().ToDataReader("u_ex.log");
while (reader.Read())
{
    var method = reader.GetString(reader.GetOrdinal("cs-method"));
    var path = reader.GetString(reader.GetOrdinal("cs-uri-stem"));
}

foreach (var directive in reader.Directives)
    Console.WriteLine($"{directive.Name}: {directive.Value}");
```

Data records must have the number of fields declared by `#Fields:`. Repeated identical field directives are accepted; changing the schema within one stream produces an `InvalidDataException` because an `IDataReader` has a stable result shape.

## Fluent configuration

Use `CommonLogReaderBuilder` or `W3cExtendedLogReaderBuilder` when you need the shared PocketCsvReader schema, resource, parser, or optimization settings.

```csharp
using PocketCsvReader.WebLogs.Configuration;

var reader = new W3cExtendedLogReaderBuilder()
    .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 16 * 1024))
    .Build();
```

Both readers support `ToDataReader`, `ToDataTable`, `ToArrayString`, and `To<T>`.
