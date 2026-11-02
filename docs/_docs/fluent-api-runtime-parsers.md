---
title: Custom parsers
tags: [configuration]
---

<span id="defining-a-set-of-parsers"></span>
<span id="handling-multiple-parsers"></span>
<span id="overview"></span>
<span id="registering-a-custom-parser"></span>

Register a field parser when conversion differs by column, or a runtime parser when the same type should be parsed consistently across fields. Both are data-reader conversion settings; `SpanObjectBuilder.SetParser` configures the separate constructor-mapping path.

## Complete custom type example

<!-- example: custom-parser -->
```csharp
using System.Globalization;
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithoutHeader().WithLineTerminator("\n"))
    .WithSchema(s => s.Indexed().WithField<Coordinate>())
    .WithParsers(p => p.WithParser((ReadOnlySpan<char> text) =>
    {
        var separator = text.IndexOf(';');
        if (separator < 0) throw new FormatException("Expected x;y");
        return new Coordinate(
            int.Parse(text[..separator], CultureInfo.InvariantCulture),
            int.Parse(text[(separator + 1)..], CultureInfo.InvariantCulture));
    }))
    .Build();
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("3;4\n"));
using var reader = csv.ToDataReader(stream);
while (reader.Read())
{
    var point = reader.GetFieldValue<Coordinate>(0);
    Console.WriteLine($"({point.X}, {point.Y})");
}
public record Coordinate(int X, int Y);
```

<!-- output: custom-parser -->
```text
(3, 4)
```

## Parser scope and precedence

| Scope | API | Effect |
| --- | --- | --- |
| Explicit getter | `GetFieldValue<T>(ordinal, parser)` | Parser for that requested read |
| Field | `WithField<T>(f => f.WithParser(...))` | Parser for one schema field |
| Field format | Specialized schema field builder | Formatted conversion, ahead of runtime type defaults |
| Runtime type | `WithParsers(p => p.WithParser(...))` | Shared type parser when no field override wins |
| Integration defaults | `UseChrononuensis()` | Fallback registrations; explicit runtime parsers win in either registration order |

The latest explicit registration for the same field/type replaces its previous parser. Schema builders can supply default numeric/temporal formats; use a field parser to override that field's format deliberately. Preserve underlying parse exceptions rather than silently substituting zero or an empty value.

## Parsing spans without intermediate strings

`ParseSpan<T>` consumes `ReadOnlySpan<char>` synchronously. Select the overload using an explicitly typed span lambda or a `ParseSpan<T>` variable. For an overloaded method group, assign it to that delegate first. With C# 13+, overload priorities retain legacy string behavior for untyped lambdas; on older compilers, type the parameter explicitly when ambiguous.

String delegates remain supported but require a string input. Typed span reads avoid that intermediate string, although returned objects and boxed value reads can allocate. Do not retain the supplied span. See [buffer ownership](../buffering-and-benchmarks/#record-lifetimes).

Nullable/reference typed reads skip custom parsers for null fields according to the getter's null policy. Array getters use registered element parsers unless a whole-array parser wins. Native JSON decoding and CSV sanitization are format-specific; see [value handling](../sequences/).

For direct use of the public `SpanParser`, `TryParse` returns false for supported conversion failures; configuration errors and unexpected failures are not a general-purpose exception suppression contract. The cell-level API is [IsValid](../validation-and-errors/#validate-a-cell).

The [Chrononuensis integration](../chrononuensis/) supplies temporal period parsers. Reproducible parser benchmarks are linked from [benchmark guidance](../buffering-and-benchmarks/#benchmarks).
