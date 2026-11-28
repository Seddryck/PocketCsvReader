---
title: Null, empty, and missing values
tags: [configuration]
---

<span id="common-use-cases-for-sequences"></span>
<span id="compression-of-repeated-values"></span>
<span id="null-sequence"></span>
<span id="placeholder-replacement"></span>

An absent column, an empty string, a configured null marker, and native JSON null are different inputs. Check the format and output API rather than assuming every getter normalizes them identically.

## Cases to distinguish

| Input situation | How to handle it |
| --- | --- |
| Label absent from a labeled record | `TryGetOrdinal` before accessing it |
| Label present with native JSON null | `IsDBNull` returns true; boxed JSON values use `DBNull.Value` |
| Explicit empty text | Usually an empty string unless applicable configured sequences replace it |
| Configured CSV null marker | Sanitized to null where special-value handling applies; non-nullable typed access can throw |
| Short CSV record | Depends on getter/output and incomplete-record options; not universally padded by every output |
| Missing selected CEF/LEEF attribute | Returns database null according to the fixed selection schema |

Nullable/reference `GetFieldValue<T>` reads follow their null policy and can return default without invoking a custom parser. Non-nullable reads can throw. Parsed dictionaries/arrays/dynamic rows normalize database nulls to C# null. Consult [DataReader access](../datareader-api/) and [output models](../output-models/) for the chosen path.

## Implementing sequences in a parser

<!-- example: null-sequence -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithResource(r => r.WithSequence("AIS", "Available in Store").WithSequence("N/A", null))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Status\nAIS\nN/A\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
    Console.WriteLine(reader.IsDBNull(0) ? "null" : reader.GetString(0));
```

<!-- output: null-sequence -->
```text
Available in Store
null
```

Use `WithSequence(pattern, stringValueOrNull)` on a resource or field builder. Replacement values are text or null, not boxed numeric values; conversion to a numeric type happens afterwards. Field and resource collections participate in sanitizer selection. Do not mutate a nonexistent `profile.Sequences` collection.

Use `WithNullDefault(value)` on a schema field to supply a typed value for inputs the reader already recognizes as null. For example, `f.WithSequence("N/A", null).WithNullDefault(0m)` returns decimal zero through typed and boxed reads and parsed rows. A non-null default makes `IsDBNull` return false. Raw field access still returns the input text.

Declare the field with a generic schema method such as `WithField<decimal?>` to have the compiler check the default against the field's type. A nullable value-type field takes its underlying type as the default, so `0m` is valid for `decimal?` and `null` is a compile-time error. Leave the default unconfigured to keep nulls. Fields declared with a runtime `Type` also use `WithNullDefault(value)`. The schema checks type compatibility when it is built.

## Replacement behavior and applicability

Sequence lookup matches an eligible complete field, not every textual occurrence. For nonempty fields, quoted or escaped input is excluded from the ordinary sequence lookup; an empty eligible value is checked separately. Thus quoted `"NULL"` and unquoted `NULL` need not mean the same thing. `HandleSpecialValues = false` disables this sanitizer path where implemented.

CSV, fixed-width after padding removal, and web/security readers applying the shared sanitizer support their configured sequences. JSON/NDJSON and LTSV/logfmt labeled readers do not use the CSV sequence sanitizer. JSON's literal `null` remains native syntax regardless of `HandleSpecialValues`; the string `"null"` remains text. Logfmt bare keys and `key=` are empty strings, not automatically null.

## Optional labels

Resolve ordinals on each current row when labels/order can change. `IsDBNull("name")` checks a present label; it is not a substitute for `TryGetOrdinal` when the label might be absent. See the [complete optional-field example](../validation-and-errors/#optional-fields-and-name-based-null-checks).
