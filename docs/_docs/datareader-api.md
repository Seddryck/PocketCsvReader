---
title: Read fields with a DataReader
tags: [usage]
---

`ToDataReader` returns the format's concrete forward-only reader. `ToDbDataReader` adapts it to the standard ADO.NET `DbDataReader`. Standard interfaces do not expose every PocketCsvReader helper.

## Obtaining an IDataReader

```csharp
using var reader = csv.ToDataReader("customers.csv");
while (reader.Read())
    Console.WriteLine(reader.GetString(reader.GetOrdinal("Name")));
```

Create `csv` with the complete [first-CSV example](../basic-usage/). The current row exists only after a successful `Read`; field counts and labels in changing labeled formats must be checked on that row. Dispose the reader even after an early exit. See [stream ownership](../stream-lifetimes/).

## Reading Values with IDataReader

### Iterating Over Records
`Read` advances synchronously. Use [ReadAsync](../async-reading/) for asynchronous I/O and cancellation. Getters remain synchronous.

### Accessing Typed Field Values
Use `GetInt32`, `GetInt64`, `GetBoolean`, `GetDecimal`, `GetString`, `GetDateTime`, or `GetFieldValue<T>` on the concrete reader. Access by ordinal or supported name overload. Getters parse on demand with field/runtime settings; a declared schema controls boxed values and reported types. See [schemas and cultures](../fluent-api-schema/).

`GetString` returns logical text after applicable format decoding/sanitization. `GetRawString` returns encoded/raw field text according to the format, including quote/escape syntax where retained. JSON/logfmt decoding differs from CSV sequence replacement; see [value handling](../sequences/).

### Generic and Custom Type Access
`GetFieldValue<T>(ordinal, parser)` and name overloads support string delegates or explicitly typed `ReadOnlySpan<char>` delegates. See [custom parsers](../fluent-api-runtime-parsers/) for complete definitions, precedence and lifetime rules. Null/reference typed reads can return default without invoking the delegate.

### Accessing Boxed Values
`GetValue`, `GetValues`, and indexers return parsed boxed values using schema/runtime settings. Untyped fields remain strings. Boxing value types allocates; a span parser does not eliminate that result allocation.

### Working with Arrays
`GetArray<T>`, `GetArray`, and `GetArrayItem<T>` expose array fields. Schema-declared one-dimensional arrays can also be returned by `GetValue`. See [array schemas](../fluent-api-schema/#arrays), including whole-array parser precedence and limitations.

### Checking for Nulls
Use `IsDBNull(index)` or the concrete reader's `IsDBNull(name)` for a present field. Use `TryGetOrdinal` first when a label might be absent; see [optional fields](../validation-and-errors/#optional-fields-and-name-based-null-checks).

## Metadata Access

| Member | Meaning |
| --- | --- |
| `GetName` / `GetOrdinal` | Current field name/index; ordinal case-sensitive matching |
| `TryGetOrdinal` | Non-throwing absent-name lookup on concrete readers |
| `GetFieldType` | Type determined by schema and reader behavior |
| `GetDataTypeName` | Configured source type name |
| `FieldCount` | Current reader shape; can vary for labeled records |

Headerless CSV generates names such as `field_0`. Do not treat a field count/name lookup before the first successful advancement as a portable operation across formats.

## Validating cells
The complete `IsValid` contract and malformed-input handling are in [validation and errors](../validation-and-errors/).

## Source positions
Enable tracking before reading; see [Source positions](../source-positions/) for units and lifecycle. [Generated row numbers](../batch-reading/#generated-row-numbers) are a separate output column.
