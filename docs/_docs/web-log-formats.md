---
title: Web log formats
subtitle: Read Common, Combined, and W3C Extended Log Format files
tags: [usage]
---

Install the `PocketCsvReader.WebLogs` extension to parse Common Log Format (CLF), Combined Log Format, and W3C Extended Log Format files.

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

### Built-in typed records

Use `ToCommonLog()` to stream `CommonLog` records without defining a custom model:

```csharp
foreach (var entry in new CommonLogReader().ToCommonLog("access.log"))
{
    DateTimeOffset? timestamp = entry.Timestamp;
    int? status = entry.StatusCode;
    long? bytes = entry.ResponseBytes;
    Console.WriteLine($"{timestamp}: {entry.Request} ({status}, {bytes} bytes)");
}
```

`CommonLog` exposes `RemoteHost`, `Identity`, `AuthenticatedUser`, and `Request` as nullable strings, `Timestamp` as `DateTimeOffset?`, `StatusCode` as `int?`, and `ResponseBytes` as `long?`. Missing values represented by `-` become null; a missing timestamp uses `[-]` and a missing request uses `"-"` to retain CLF delimiters.

Timestamps are parsed using the CLF pattern `dd/MMM/yyyy:HH:mm:ss zzz` and invariant culture, preserving the original UTC offset (for example, `-0700`). Numbers also use invariant culture. Invalid typed values throw `FormatException`; numbers outside their target type's range throw `OverflowException` during enumeration.

Both filename and `Stream` overloads return `IEnumerable<CommonLog>`. Records are parsed as enumeration advances; the reader disposes its input when enumeration completes, fails, or the enumerator is disposed early. As with `To<T>()`, the filename overload opens the file when called, so enumerate the result and dispose the enumerator (for example, with `foreach`). The existing `To<T>()` remains available for custom models and mappers.

## Combined Log Format

Select `CombinedLogReader` for the nine-field Combined format: the seven CLF fields followed by quoted `Referrer` and `UserAgent` fields. `CommonLogReader` continues to require exactly seven fields.

```csharp
using PocketCsvReader.WebLogs;

using var reader = new CombinedLogReader().ToDataReader("access.log");
while (reader.Read())
    Console.WriteLine(reader.GetString(reader.GetOrdinal("UserAgent")));

foreach (CombinedLog entry in new CombinedLogReader().ToCombinedLog("access.log"))
    Console.WriteLine($"{entry.Timestamp}: {entry.Referrer} ({entry.UserAgent})");
```

`CombinedLog` exposes the same nullable properties and invariant conversions as `CommonLog`, plus nullable string `Referrer` and `UserAgent` properties. `ToCombinedLog(string filename)` and `ToCombinedLog(Stream stream)` return `IEnumerable<CombinedLog>` with the same streaming and disposal behavior as `ToCommonLog()`, including disposal on completion, conversion failure, or early exit. Timestamp offsets are preserved, and invalid typed values throw `FormatException` or `OverflowException` during enumeration.

Requests, referrers, and user agents retain spaces and backslash-escaped quote syntax, as in the CLF reader. Use `"-"` for missing quoted fields. Typed `CombinedLog` output converts these to null; generic and tabular outputs preserve quoted hyphens, matching existing CLF behavior. Unquoted hyphens become null. Malformed records throw `InvalidDataException` with the format name and physical line number.

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

Use `CommonLogReaderBuilder`, `CombinedLogReaderBuilder`, or `W3cExtendedLogReaderBuilder` when you need the shared PocketCsvReader schema, resource, parser, or optimization settings. A custom `WebLogProfile` passed to `CombinedLogReader` must select `WebLogFormat.Combined`.

```csharp
using PocketCsvReader.WebLogs.Configuration;

var reader = new W3cExtendedLogReaderBuilder()
    .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 16 * 1024))
    .Build();
```

All three readers support `ToDataReader`, `ToDataTable`, `ToArrayString`, and `To<T>`.

## Streaming storage

CLF and W3C readers frame records over shared pooled character buffers and reuse field metadata for their fixed result schema. A record contained in one buffer is read as a view over that memory; a record spanning buffers is consolidated once into pooled storage. The current record's character memory and metadata are valid until the next read or reader disposal. Strings and typed objects returned to callers own their materialized values and remain valid after the reader advances.

Synchronous and asynchronous reads use the same framing and field-parsing logic. W3C directive names and values remain retained strings, and string/object output still allocates the values required by the public API.
