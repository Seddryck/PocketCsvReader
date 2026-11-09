---
title: LTSV and logfmt
subtitle: Read logfmt and Labeled Tab-separated Values (LTSV)
tags: [quick-start, formats]
---

Install `PocketCsvReader.KeyValue` to read records whose fields carry their own labels. Use `LogfmtReaderBuilder` for space-separated `key=value` records or `LtsvReaderBuilder` for tab-separated `label:value` records.

```bash
dotnet add package PocketCsvReader.KeyValue
```

## Example: read application logs with logfmt

This complete example reads two application log entries. The second entry deliberately changes the field order, so the reader resolves labels on each row.

<!-- example: logfmt -->
```csharp
using System;
using System.IO;
using System.Text;
using PocketCsvReader.KeyValue.Configuration;

var content = "level=info message=\"Order accepted\" order_id=1001\n"
            + "order_id=1002 message=\"Payment pending\" level=warn\n";

var logs = new LogfmtReaderBuilder().Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
using var reader = logs.ToDataReader(stream);
while (reader.Read())
{
    var level = reader.GetString(reader.GetOrdinal("level"));
    var orderId = reader.GetInt32(reader.GetOrdinal("order_id"));
    var message = reader.GetString(reader.GetOrdinal("message"));
    Console.WriteLine($"{level}: order {orderId}, {message}");
}
```

Output:

<!-- output: logfmt -->
```text
info: order 1001, Order accepted
warn: order 1002, Payment pending
```

To read a file, replace `logs.ToDataReader(stream)` with `logs.ToDataReader("application.log")`.

Logfmt values can be unquoted or double-quoted. Quoted values support escapes such as `\"`, `\\`, and `\n`. `GetString` returns the decoded value; `GetRawString` retains the original field value, including its quotes and escapes. Bare keys such as `active` and explicit empty values such as `message=` return empty strings.

## Example: read LTSV request logs

LTSV separates fields with a tab and splits each field at its first colon. A value can contain further colons, as in a URL or timestamp. This example includes the same labels in different orders:

<!-- example: ltsv -->
```csharp
using System;
using System.IO;
using System.Text;
using PocketCsvReader.KeyValue.Configuration;

var content = "host:example.org\tstatus:200\tpath:/orders/1001\n"
            + "path:/orders/1002\thost:example.org\tstatus:404\n";

var requests = new LtsvReaderBuilder().Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
using var reader = requests.ToDataReader(stream);
while (reader.Read())
{
    var status = reader.GetInt32(reader.GetOrdinal("status"));
    var path = reader.GetString(reader.GetOrdinal("path"));
    Console.WriteLine($"{status}: {path}");
}
```

Output:

<!-- output: ltsv -->
```text
200: /orders/1001
404: /orders/1002
```

## Labels and changing record shapes

Labels come from each record, rather than a header row. Call `Read()` before looking up a field. `FieldCount`, field order, and labels may change between records; resolve ordinals on the current row instead of caching them across rows.

Use `TryGetOrdinal("label", out var ordinal)` to check for optional fields before accessing them. Duplicate labels are preserved, and `GetOrdinal` returns the first matching occurrence. Access subsequent occurrences by index. A missing label passed to `GetOrdinal` throws `ArgumentOutOfRangeException`.

Both formats accept LF, CRLF, or CR line endings and a final record without a newline. An empty line is exposed as a record with zero fields. Invalid syntax throws `InvalidDataException`.

## Project a fixed set of columns

Use `WithProjection` on either builder to select source keys in output order and optionally give them aliases.

```csharp
var requests = new LogfmtReaderBuilder()
    .WithProjection(columns => columns
        .Column("order_id", "OrderId")
        .Column("level")
        .Column("message"))
    .Build();
```

The configured names and ordinals stay the same even when source fields move or are absent. A missing key is exposed as `DBNull.Value`; a present empty value remains an empty string. When a key occurs more than once, projection takes its first value. Output names must be unique. Named schemas and filters refer to the output names. Unselected fields are still checked for valid syntax.

## Configuration and output models

Both builders support the shared `WithSchema`, `WithResource`, `WithParsers`, and `WithParserOptimizations` settings. Use a named schema when field types should follow labels as their positions change. See [Schema configuration](../fluent-api-schema/) and [Resource configuration](../fluent-api-resource/) for details.

The readers expose `ToDataReader`, `ToDbDataReader`, `ToDataTable`, and `ToArrayString`, with filename and stream overloads. `ToDataReader` is useful for variable record shapes; tabular output requires a consistent set of columns. They also provide the shared parsed-row materializers such as `ToDictionaries` and `ToObjectArrays`.

Input is processed in one forward-only pass, including non-seekable streams. Read-ahead and row-count pre-scans are disabled for these formats. Keep the reader open while accessing its current row, and dispose it when finished. Materialized strings and output objects remain valid after the next read.

For null versus empty values see [value handling](../sequences/); for reused buffers and metadata see [key-value buffering](../buffering-and-benchmarks/#key-value-records).
