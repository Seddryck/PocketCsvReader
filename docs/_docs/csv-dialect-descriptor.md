---
title: CSV and TSV
tags: [formats]
---

<span id="casesensitiveheader"></span>
<span id="commentchar"></span>
<span id="commentrows"></span>
<span id="csvddfversion"></span>
<span id="delimiter"></span>
<span id="doublequote"></span>
<span id="escapechar"></span>
<span id="header"></span>
<span id="headerjoin"></span>
<span id="headerrows"></span>
<span id="lineterminator"></span>
<span id="nullsequence"></span>
<span id="quotechar"></span>
<span id="skipinitialspace"></span>

The core `PocketCsvReader` package reads delimiter-separated records. Configure a `DialectDescriptor` through `CsvReaderBuilder.WithDialect` before reading.

## Example: semicolon export

<!-- example: semicolon-csv -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithDelimiter(';').WithLineTerminator("\n").WithHeader())
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id;Name\n1;\"Ada; Lovelace\"\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
```

<!-- output: semicolon-csv -->
```text
1: Ada; Lovelace
```

Use `WithDelimiter('\t')` for TSV. Quoted fields may contain delimiters or record terminators; doubled quote escaping is enabled by default. For backslash escaping configure `WithEscapeChar('\\')` explicitly. A final record can end at EOF without a terminator. Use a configured terminator matching the input; do not assume every newline convention is interchangeable.

## Dialect reference

These are `new DialectDescriptor()` defaults, not necessarily the defaults of a preset profile.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Delimiter` | comma | Field separator |
| `LineTerminator` | `"\r\n"` | Record separator; quoted records may span physical lines |
| `QuoteChar` | `"` | Quote character; `WithoutQuoteChar()` disables it |
| `DoubleQuote` | `true` | Doubled quotes inside quoted text represent one quote |
| `EscapeChar` | `null` | Optional alternate escape character |
| `NullSequence` | `null` | Configured representation of null; see [value handling](../sequences/) |
| `MissingCell` | `null` | Missing-cell representation |
| `SkipInitialSpace` | `false` | Ignore configured initial field spaces |
| `Header` | `true` | First configured header row supplies names |
| `HeaderRows` | `[1]` | One-based header row indexes |
| `HeaderJoin` | one space | Join corresponding cells from multiple header rows |
| `HeaderRepeat` | `true` | Each file in a batch has its own headers |
| `CommentChar` | `null` | Optional comment marker |
| `CommentRows` | `null` | Optional one-based rows treated as comments |
| `ArrayDelimiter`, `ArrayPrefix`, `ArraySuffix` | `null` | Optional array syntax inside a field |

`DialectDescriptor` has no `CaseSensitiveHeader` or `CsvDdfVersion` setting. Reader name lookup uses ordinal, case-sensitive comparison. The dialect vocabulary is related to the [Frictionless CSV dialect specification](https://specs.frictionlessdata.io/csv-dialect/); the table above describes the actual public API.

### Headers and comments

Use `WithHeaderRows(new[] { 1, 2 }).WithHeaderJoin(".")` for multiple header rows. `WithoutHeader()` clears header rows. Headerless CSV uses generated names such as `field_0`. Repeated headers and generated row numbers are covered in [batch reading](../batch-reading/).

Configure comments through `WithCommentChar('#')` or `WithCommentRows(new[] { 1, 3 })`. The parser distinguishes comments from quoted data; comment markers inside quoted fields remain data. Comments do not produce returned data rows. Test comment placement against representative input instead of stripping text before parsing.

### Arrays

`WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']'))` configures array syntax. Declare array types in the [schema](../fluent-api-schema/#arrays), or use explicit array getters. An array declaration does not transform arbitrary scalar text into an array.

## Presets

[Earlier preset-page links](../pre-configured-dialect-descriptor/) remain supported; this section owns the current preset reference.

`CsvProfile` provides the following presets. They use `Environment.NewLine`, backslash escaping, doubled-quote support, and **no header**.

| Profile | Delimiter | Quote |
| --- | --- | --- |
| `CommaDoubleQuote` | `,` | `"` |
| `SemiColumnDoubleQuote` | `;` | `"` |
| `TabDoubleQuote` | tab | `"` |
| `PipeSingleQuote` | `\|` | `'` |

### CommaDoubleQuote
Use `new CsvReader(CsvProfile.CommaDoubleQuote)` for the comma preset.
### SemiColumnDoubleQuote
Use `new CsvReader(CsvProfile.SemiColumnDoubleQuote)` for the semicolon preset; the public name retains this spelling.
### TabDoubleQuote
Use `new CsvReader(CsvProfile.TabDoubleQuote)` for TSV.
### PipeSingleQuote
Use `new CsvReader(CsvProfile.PipeSingleQuote)` for pipe-separated, single-quoted input.

## Configuration errors

`Build()` rejects an empty terminator, non-positive header row numbers, enabled headers without header rows, disabled headers with header rows, colliding delimiter/quote/comment characters, and reserved syntax characters occurring inside the line terminator. These are configuration errors, not recoverable bad records.

For data errors see [validation and recovery](../validation-and-errors/). For conversion, nulls, and incomplete rows see [schemas](../fluent-api-schema/) and [null and missing values](../sequences/). Custom terminator mismatch fixes preserve parsing state; they do not make mismatched terminators a supported substitute for a correctly configured dialect.
