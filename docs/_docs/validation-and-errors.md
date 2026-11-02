---
title: Validate values and handle malformed input
tags: [diagnostics]
---

Separate three failures: invalid configuration (before reading), malformed record syntax (while advancing), and invalid field conversion (when requesting a typed value). A recovery policy for CSV syntax does not automatically catch custom parser exceptions.

## Optional fields and name-based null checks

<!-- example: optional-fields -->
```csharp
using System.Text;
using PocketCsvReader.Ndjson.Configuration;

var json = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
    "{\"name\":\"Ada\"}\n{\"name\":null}\n{\"id\":3}\n"));
using var reader = json.ToDataReader(stream);
while (reader.Read())
{
    if (!reader.TryGetOrdinal("name", out var ordinal)) Console.WriteLine("absent");
    else if (reader.IsDBNull("name")) Console.WriteLine("null");
    else Console.WriteLine(reader.GetString(ordinal!.Value));
}
```

<!-- output: optional-fields -->
```text
Ada
null
absent
```

`TryGetOrdinal` returns false and a null ordinal for an absent label. `GetOrdinal` throws for absent names. Re-resolve after `Read()` for formats whose shape changes. These helpers belong to concrete readers rather than the standard `IDataReader` interface.

## Validate a cell

Concrete readers and CSV batches expose `IsValid(index)` and `IsValid(name)`. Validation uses the type reported by `GetFieldType`, its parser, format and applicable sanitization. Untyped string fields can be valid even when the text is not a date. Configure a date schema before using validation to check date syntax.

Expected conversion failures return false. Invalid names/ordinals, calls without a current row, configuration errors, and unexpected custom-parser failures still throw. Validation neither advances the reader nor caches the converted cell. Custom parsers run on each call and should avoid side effects. A later getter can still throw even after a prior validation attempt.

Null/missing values follow existing boxed-getter behavior: a CSV sanitized null that its boxed getter rejects is invalid, whereas native JSON null returned as `DBNull.Value` is valid. Arrays without whole-array overrides validate their elements. See [null and missing values](../sequences/).

## Recover from malformed CSV

<!-- example: bad-data -->
```csharp
using System.Text;
using PocketCsvReader;
using PocketCsvReader.Configuration;

var profile = new CsvProfile(new DialectDescriptorBuilder()
    .WithoutHeader().WithLineTerminator("\r\n").Build())
{
    BadDataPolicy = new BadDataPolicy(context => BadDataAction.SkipRecord, maximumErrors: 2)
};
var csv = new CsvReader(profile);
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n\"bad\"x,z\r\nc,d"));
foreach (var row in csv.ToArrayString(stream))
    Console.WriteLine(string.Join(",", row));
```

<!-- output: bad-data -->
```text
a,b
c,d
```

| Action | Behavior |
| --- | --- |
| `Throw` | Propagate the malformed-record error; default without a recovery policy |
| `SkipRecord` | Discard the malformed record and continue at recovery boundary |
| `ReturnPartialRecord` | Return fields recovered from the malformed record; consumers must accept incomplete data |
| `Stop` | Stop reading at the error |

`MaximumErrors` limits reported malformed-input events. Exceeding it throws before another handler decision. Recovery is a CSV character-parser policy, not a promise that arbitrary corrupted input can be reconstructed. Native UTF-8 `Auto` falls back for profiles using a bad-data policy; forced byte mode rejects them. Other formats retain their documented error behavior.

## Diagnostics and configuration errors

`BadDataContext` supplies record/line information, field index, offset, parser state, offending input and the exception. Enable `WithPositionTracking()` before reading for precise record starts and explicit offset units; see [source positions](../source-positions/). Legacy diagnostics without tracking can be approximate/record-relative.

Dialect `Build()` rejects invalid combinations described in [CSV configuration errors](../csv-dialect-descriptor/#configuration-errors). Fix configuration rather than trying to recover it as a data row. After an unrecovered parser error or input-I/O cancellation, stop using the reader and restart with a fresh reader and replayable input. Getter conversion errors do not themselves consume another record; handle them deliberately at the application level.

[Reader filtering](../reader-filtering/) selects rows; it does not suppress
conversion failures, configuration errors, or predicate exceptions. To reject
malformed cell text deliberately, use a span predicate with `TryParse`. Rejected
rows retain their source numbering for diagnostics.
