---
title: Schema and Dialect Inference
tags: [configuration]
---

PocketCsvReader can inspect a bounded sample and return an explicit schema and dialect. Inference is opt-in: it never changes an existing reader or silently replaces an explicitly configured schema.

## Infer a Schema

When the dialect is known, supply it and leave dialect inference disabled:

```csharp
var options = new CsvInferenceOptions
{
    MaximumRecords = 1_000,
    Header = CsvHeaderMode.Auto,
    Dialect = new DialectDescriptorBuilder()
        .WithDelimiter(Delimiter.Semicolon)
        .WithLineTerminator(LineTerminator.LineFeed)
        .WithoutHeader()
        .Build()
};

var result = CsvInference.Infer("input.csv", options);

var reader = new CsvReaderBuilder()
    .WithDialect(result.Dialect)
    .WithSchema(result.Schema)
    .Build();
```

`Header` can be `Auto`, `Present`, or `Absent`. The explicit modes take precedence over header inference. `MaximumBytes` can impose an additional raw-input byte limit.

## Infer the Schema and Dialect

Dialect inference evaluates comma, tab, semicolon, and pipe delimiters together with common quote behavior. It also detects CRLF, LF, and CR line terminators. The selected dialect and a confidence diagnostic are returned rather than applied to another reader automatically.

```csharp
var result = CsvInference.Infer("unknown.csv", new CsvInferenceOptions
{
    MaximumRecords = 500,
    InferDialect = true,
    Culture = CultureInfo.GetCultureInfo("fr-FR")
});

foreach (var diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
```

Use `Resource` to configure an encoding, compression, or null sequences. Filename inference recognizes supported compression extensions. Parsing of comments, quoted multiline values, escapes, encodings, and compressed input uses the normal CSV parser pipeline.

## Type Promotion

Inference uses the configured culture and this deterministic order:

1. `Boolean`
2. `Int16`, `Int32`, then `Int64`
3. `Decimal`, then `Double`
4. `DateOnly`, `TimeOnly`, `DateTimeOffset`, then `DateTime`
5. `Guid`
6. `String`

A candidate type is selected only when every non-null sampled value can be represented by it. Incompatible values fall back to `String`. Digit sequences with significant leading zeroes remain strings. A column containing only null-like values also falls back to `String` and produces a diagnostic.

Each `CsvInferredColumn` reports value, null, empty, and missing counts. Nullability is copied to `FieldDescriptor.IsNullable`; the runtime type remains the underlying type used by the reader.

## Streams

Seekable streams are restored to their original position by default, allowing the inferred result to be used without losing sampled records. Set `RestoreStreamPosition` to `false` to retain the parser's final buffered position. Inference-only calls consume non-seekable streams; callers that need to read them afterward should buffer or otherwise replay the input.

Header and dialect detection are heuristic. Review diagnostics with codes such as `header.ambiguous`, `dialect.ambiguous`, and `records.inconsistent-width` before accepting inference for unattended ingestion.
