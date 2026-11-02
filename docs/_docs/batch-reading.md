---
title: Read multiple files and number rows
tags: [usage]
---

CSV can expose several inputs as one forward-only `CsvBatchDataReader`. Supply filenames, already-open streams, or functions that open streams. Stream factories avoid opening every file up front.

## Example with stream factories

<!-- example: batch-reader -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithRowNumber()
    .Build();
Func<Stream>[] inputs =
[
    () => new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n")),
    () => new MemoryStream(Encoding.UTF8.GetBytes("Name\nGrace\n"))
];
using var reader = csv.ToDataReader(inputs);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
```

<!-- output: batch-reader -->
```text
1: Ada
2: Grace
```

For files, call `csv.ToDataReader(new[] { "part1.csv", "part2.csv" })`. `ToDbDataReader` has corresponding batch overloads; use `ReadAsync(token)` for async advancement. Factories are invoked synchronously when moving to an input; they are not asynchronous stream factories.

## Headers and schema

By default, each file has its own header (`HeaderRepeat = true`). Use `WithHeaderRepeat(false)` when only the first file supplies a header; the batch carries those names forward. All inputs use the same profile. Keep column order, conversion schema, and header expectations compatible across files.

## Ownership

Batch construction opens its first input. Advancing closes the previous stream; disposing closes the current stream and releases the batch enumerator. The batch owns provided streams and streams returned by factories. Prefer factories for many files and dispose a batch even if no records are read. Do not reuse a consumed stream sequence without resetting/reopening it.

## Generated row numbers

`WithRowNumber()` prepends an `int` column named `rowNumber`; `WithRowNumber("recordId")` chooses its name, and `WithoutRowNumber()` disables it. The last call wins. Null, empty, or whitespace names are rejected. A name exactly colliding with a source column throws `InvalidDataException`.

Numbers start at one for each reader and continue across batch inputs. Headers, comments, and records skipped by malformed-data recovery do not consume a generated row number. [Reader filters](../reader-filtering/) run after numbering, so filtered-out data records consume a number and leave gaps. Multiline CSV records receive one number. Overflow beyond `Int32` throws.

Constructor mapping requires an additional first parameter for the generated value; custom CSV span mappers receive its field first. Tables and parsed row outputs expose an integer; string arrays expose a decimal string. Source schema indexes remain indexes of the original columns. Generated row numbers differ from [source positions](../source-positions/), which identify source records and offsets and can count skipped malformed records differently.
