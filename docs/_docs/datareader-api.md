---
title: DataReader API
tags: [quick-start]
---

The [`IDataReader` interface](https://learn.microsoft.com/en-us/dotnet/api/system.data.idatareader) provides a forward-only stream of rows. This model is memory-efficient because it reads one row at a time. PocketCsvReader supports both its existing `IDataReader`-based API and the standard [`DbDataReader`](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatareader) abstraction.

This behavior matches the use case for reading delimited files, where you typically want to iterate through rows sequentially.

## Obtaining an IDataReader

For delimited files like `file.csv`, the most straightforward way to obtain an `IDataReader` is through the `CsvReader` class, using the `ToDataReader(string filename)` method (or an overload that accepts a stream). Dialect configuration (e.g., delimiters and line terminators), encoding, and compression are provided through a `CsvProfile` object.

```csharp
using var stream = File.OpenRead(filename);
var profile = new CsvProfile(
    new DialectDescriptorBuilder()
        .WithDelimiter(',')
        .WithLineTerminator("\n")
        .WithHeader()
        .Build(),
    null,
    new ResourceDescriptorBuilder()
        .WithEncoding("utf-8")
        .WithCompression("gz")
        .Build());
using var reader = new CsvReader(profile).ToDataReader(stream);
while (reader.Read())
{
    Console.WriteLine(reader.GetInt32(0));
    Console.WriteLine(reader.GetString(1));
}
```

For code that consumes the standard `DbDataReader` abstraction, use `ToDbDataReader`. It is available for CSV, fixed-width, NDJSON, key-value, and web-log readers, as well as CSV batches. The adapter delegates to the same parser and schema implementation as `ToDataReader`.

```csharp
await using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
await using DbDataReader reader = new CsvReader(profile).ToDbDataReader(stream);
while (await reader.ReadAsync(cancellationToken))
{
    Console.WriteLine(reader.GetInt32(0));
    Console.WriteLine(reader.GetString(1));
}
```

Typed getters remain synchronous: once `ReadAsync` completes, the current record is buffered and parsed in memory. Do not call `Read` and `ReadAsync` concurrently on the same reader.

`ReadAsync` performs asynchronous stream I/O and forwards cancellation. `CloseAsync` and `DisposeAsync` asynchronously release the underlying reader and stream. `HasRows` may read and buffer the first record; the following `Read` or `ReadAsync` still returns that record.

For strongly typed CSV consumers, `CsvReader.ToAsync<T>` builds on this same asynchronous reader and parser path and exposes it directly as `IAsyncEnumerable<T>`. Use `await foreach` when you want record mapping without managing a data reader; use `ToDbDataReader` when an ADO.NET-compatible consumer needs field-by-field access.

## Reading Values with IDataReader

### Iterating Over Records

Use `Read()` for synchronous advancement or `ReadAsync(CancellationToken)` for asynchronous advancement. Both return `true` if another row is available and `false` once the end of the stream is reached.

### Accessing Typed Field Values

The `IDataReader` interface provides type-specific accessors such as `GetInt32`, `GetString`, and `GetDateTime`. You can access a column by index (zero-based) or by name. If the schema is not provided and no header is present, field names are defaulted to `field_{index}`.

- `GetString` reads the value after unescaping and removing quotes, returning it as-is.
- Type-specific methods like `GetInt32` or `GetDateTime` rely on built-in parsers to convert text into structured data.

| **Method**            | **Return Type**     | **Default Parser**         |
|----------------------|---------------------|----------------------------|
| `GetBoolean`         | `bool`              | `Boolean.Parse`           |
| `GetInt16`           | `short`             | `Int16.Parse`             |
| `GetInt32`           | `int`               | `Int32.Parse`             |
| `GetInt64`           | `long`              | `Int64.Parse`             |
| `GetFloat`           | `float`             | `float.Parse`             |
| `GetDouble`          | `double`            | `double.Parse`            |
| `GetDecimal`         | `decimal`           | `decimal.Parse`           |
| `GetDateOnly`        | `DateOnly`          | `DateOnly.Parse`          |
| `GetTimeOnly`        | `TimeOnly`          | `TimeOnly.Parse`          |
| `GetDateTime`        | `DateTime`          | `DateTime.Parse`          |
| `GetDateTimeOffset`  | `DateTimeOffset`    | `DateTimeOffset.Parse`    |
| `GetGuid`            | `Guid`              | `Guid.Parse`              |

Numeric formats allow signs and scientific notation (e.g. `10e9`). Decimal separator is `.` and there is no thousands separator. Temporal formats follow ISO 8601 conventions.

Parser settings can be overridden via schema definitions or fluent API as explained in the [Fluent API Schema](/docs/fluent-api-schema) documentation.

### Generic and Custom Type Access

The methods `GetFieldValue<T>(int)` and `GetFieldValue<T>(string)` work similarly to type-specific ones. They rely on type inference to determine the correct parser.

- Example: `GetFieldValue<int>("foo")` and `GetInt32("foo")` yield the same result.
- Parsers can be customized per field or per type via schema or by supplying a `Func<string, T>`.

```csharp
reader.GetFieldValue<YearMonth>("foo", raw => YearMonth.Parse(raw));
```

This allows support for user-defined types like `YearMonth`.

See also: [Providing a Custom Parser](/docs/fluent-api-schema#providing-a-custom-parser).

### Accessing Boxed Values

- `GetValue(int)` returns a field as an `object`, boxing the parsed value.
- `GetValues(object[])` populates an array with the values of the current row.
- Indexers like `reader[10]` or `reader["foo"]` are shorthand for `GetValue`.

### Working with Arrays

- `GetArray<T>(int)` parses a field as an array of `T`.
- `GetArray(int)` returns a field as an array of `object`.
- `GetArrayItem<T>(int fieldIndex, int itemIndex)` retrieves a single array element. Out-of-bound access in arrays raises `ArgumentOutOfRangeException`.

### Checking for Nulls

Most accessors return non-nullable types and expect a valid value. Use `IsDBNull(int)` to check if a field is null before reading.

## Metadata Access

The following members return information about fields:

- `GetName(int)` — Gets the column name.
- `GetOrdinal(string)` — Gets the zero-based column ordinal.
- `GetFieldType(int)` — Gets the field's CLR type.
- `GetDataTypeName(int)` — Gets the data type name from schema.
- `FieldCount` — Total number of columns expected from schema and headers.
