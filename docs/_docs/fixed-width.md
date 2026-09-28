---
title: Fixed-width files
tags: [quick-start, fixed-width]
---

Install the `PocketCsvReader.FixedWidth` package to process text records whose fields occupy fixed character positions. Define each field with a zero-based offset and a positive width, then build a `FixedWidthReader`:

```bash
dotnet add package PocketCsvReader.FixedWidth
```

```csharp
using PocketCsvReader.FixedWidth;
using PocketCsvReader.FixedWidth.Configuration;

var fixedWidth = new FixedWidthReaderBuilder()
    .WithLineTerminator("\n")
    .WithField("Id", offset: 0, width: 5,
        padding: FixedWidthPadding.Left, paddingChar: '0')
    .WithField("Name", offset: 6, width: 15)
    .WithField("BirthDate", offset: 21, width: 10,
        padding: FixedWidthPadding.None)
    .WithSchema(schema => schema.Indexed()
        .WithField<int>()
        .WithField<string>()
        .WithTemporalField<DateOnly>(field => field.WithFormat("yyyy-MM-dd")))
    .Build();

using var reader = fixedWidth.ToDataReader("people.txt");
while (reader.Read())
{
    var id = reader.GetInt32(0);
    var name = reader.GetString(1);
    var birthDate = reader.GetFieldValue<DateOnly>(2);
}
```

Fields may be adjacent or separated by ignored gaps. Overlapping fields, negative offsets, and non-positive widths are rejected when the reader is built. Records must exactly match the end of the last declared field unless `AllowTrailingCharacters()` is enabled.

Offsets and widths count decoded .NET characters (UTF-16 code units), not encoded bytes. Fixed-width binary data, EBCDIC byte positions, and COBOL copybooks are not supported.

## Raw values and padding

`GetRawString` returns the exact field slice, including padding. Normal value access removes only the padding configured for that field:

- `None` preserves both sides.
- `Left` removes the configured padding character from the left.
- `Right` removes it from the right and is the default.
- `Both` removes it from both sides.

Sequence replacement, null handling, schema formats, and custom parsers run after padding is removed.

## Single-pass and lazy parsing guarantees

`FixedWidthDataReader` processes the source in one forward-only pass and supports non-seekable streams. It never pre-scans, rewinds, or loads the complete file into memory. Memory is bounded by the configured read buffer plus the current record.

`Read()` consumes and validates only the next record and records the configured field spans. It does not materialize, sanitize, or convert every field. A field's padding, sequences, null value, and typed parser are evaluated only when that field is requested through a `Get*` accessor. Requesting one field does not parse another, so an invalid value in an unused field does not fail the record.

The stream still has to consume the characters occupied by unused fields; lazy parsing means that those characters are not interpreted or allocated as field values.

## Other output models

The same reader supports the standard output models:

```csharp
DataTable table = fixedWidth.ToDataTable("people.txt");
IEnumerable<string?[]> rows = fixedWidth.ToArrayString("people.txt");
IEnumerable<Person> people = fixedWidth.To<Person>("people.txt");
```

The default object mapper uses a public constructor with one parameter per field. Pass a mapping function when only selected fields are needed; only the fields accessed by that function are parsed:

```csharp
var names = fixedWidth.To("people.txt",
    row => row.GetString(row.GetOrdinal("Name")));
```
