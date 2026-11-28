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

| Accessor | Result |
| --- | --- |
| `GetRawString(ordinal)` | Encoded field text, including quotes and escapes where the format retains them. |
| `GetDecodedString(ordinal)` | Field text after decoding, unescaping, and sanitization. Skips schema defaults and conversions. Returns an empty string for a decoded empty field and throws `InvalidCastException` for a source null. Use `IsSourceNull` before reading decoded text. |
| `GetString(ordinal)` | Reads the field as a string. Applies a configured string default for a source null or uses the same parser precedence as `GetFieldValue<string>(ordinal)`. Otherwise, reads the decoded text. |

Use `GetDecodedString` to inspect input such as a malformed date after a typed getter throws. For format-specific decoding and sanitization rules, see [value handling](../sequences/).

### Generic and Custom Type Access
`GetFieldValue<T>(ordinal, parser)` and name overloads support string delegates or explicitly typed `ReadOnlySpan<char>` delegates. See [custom parsers](../fluent-api-runtime-parsers/) for complete definitions, precedence and lifetime rules. Null/reference typed reads can return default without invoking the delegate.

### Accessing Boxed Values
`GetValue`, `GetValues`, and indexers return parsed boxed values using schema/runtime settings. Untyped fields remain strings. Boxing value types allocates; a span parser does not eliminate that result allocation.

### Working with Arrays
`GetArray<T>`, `GetArray`, and `GetArrayItem<T>` expose array fields. Schema-declared one-dimensional arrays can also be returned by `GetValue`. See [array schemas](../fluent-api-schema/#arrays), including whole-array parser precedence and limitations.

### Checking for Nulls
`IsDBNull(index)` and `IsDBNull(name)` report whether the effective value is null. When `WithNullDefault` supplies a value, `IsDBNull` returns `false` and compatible typed and boxed getters return the default.

Use `IsSourceNull(ordinal)` or `IsSourceNull(name)` on the concrete reader to check whether the input was recognized as null before a default was applied. For example, with `N/A` configured as a null marker and `0m` as the default, `IsSourceNull` returns `true`, `IsDBNull` returns `false`, and `GetDecimal` returns zero. `GetRawString` still exposes the original marker. `ToArrayString` and text-mode `ToDataTable` preserve source nulls.

Computed fields have no source text. For these fields, `IsSourceNull` reports whether the computed result is null. Use `TryGetOrdinal` first when a label might be absent. See [optional fields](../validation-and-errors/#optional-fields-and-name-based-null-checks).

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

## Filter before reading

Configure [reader filters](../reader-filtering/) on the builder to make `Read()` and
`ReadAsync()` skip rejected rows before returning a current record. Conditions can
inspect sanitized spans, parsed values, or several fields through a read-only row
view. Compatible parsed values are reused by subsequent getters.
