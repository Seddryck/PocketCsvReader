---
title: IDataReader API
tags: [quick-start]
---

The [`IDataReader` interface](https://learn.microsoft.com/en-us/dotnet/api/system.data.idatareader?view=net-9.0) is a .NET interface designed to provide a way to read a forward-only stream of rows from a data source. This model is particularly efficient for memory usage because it reads one row at a time and does not buffer the entire result set.

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

## Reading Values with IDataReader

### Iterating Over Records

Use the `Read()` method to advance to the next row. It returns `true` if another row is available, and `false` once the end of the stream is reached.

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
