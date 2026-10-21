---
title: Fluent API for Runtime Type Parsers
tags: [configuration]
---

## Overview

The Fluent API for runtime type parsers in PocketCsvReader provides an intuitive and flexible way to define parsers for various runtime types. This is particularly useful when working with custom or non-standard types serialized in a CSV file that need to be correctly deserialized.

By defining a set of parsers, you ensure that these types are always deserialized properly across different CSV files. However, if the deserialization logic varies depending on the field, you should register the parser at the field level instead. See: [Providing a Custom Parser](/docs/fluent-api-schema#providing-a-custom-parser).

## Defining a set of parsers

You can configure runtime type parsers in two ways:

1. Using a CSV Profile

The `CsvProfile` class allows you to define a set of parsers as part of the CSV processing configuration:

```csharp
var profile = new CsvProfile(
    new DialectDescriptorBuilder().Build()
    , new SchemaDescriptorBuilder()
            .Indexed()
            .WithField<YearMonth>()
            .Build()
    , null
    , new RuntimeParsersDescriptorBuilder()
            .WithParser(...)
            .Build()
);
```

1. Using a CSV Reader Builder

You can also configure parsers using the `CsvReaderBuilder` class:

```csharp
var builder = new CsvReaderBuilder().WithParsers
(
    new RuntimeParsersDescriptorBuilder()
                .WithParser(...)
);
var reader = builder.Build();
```

### Registering a Custom Parser

To define custom parsers, instantiate a `RuntimeParsersDescriptorBuilder` and use its method `WithParser` method.

**Example:** Custom Parser for `Point`

```csharp
Point parse(string input)
{
    var parts = input.Split(';');
    return new Point(parts[0], parts[1]);
}

var schema = new RuntimeParsersDescriptorBuilder()
    .WithParser<Point>(parse)
    .Build();
```

In this example:

- The custom parser splits the input string using ; as a delimiter.
- It parses the extracted values into integers.
- It creates a Point instance from the parsed values.

### Handling Multiple Parsers

- You can define multiple parsers for different types.
- If two parsers are registered for the same type, the most recently registered parser overrides the previous one.

### Parsing spans without intermediate strings

Use the public `PocketCsvReader.ParseSpan<T>` delegate to consume a `ReadOnlySpan<char>` directly:

```csharp
ParseSpan<int> parse = (ReadOnlySpan<char> value) =>
    int.Parse(value, CultureInfo.InvariantCulture);

var parsers = new RuntimeParsersDescriptorBuilder().WithParser(parse).Build();

// Direct descriptor registration is also supported.
var descriptor = new RuntimeParsersDescriptor();
descriptor.AddParser(parse);
```

Field parsers and field formats retain precedence over runtime parsers. A schema builder may supply a default numeric or temporal format; use field-level registration to override that format.

The parser runs synchronously and must not retain the span. It receives the same sanitized and decoded input as existing string parsers. Typed reads avoid intermediate strings; boxed reads still allocate when boxing value types, and returned objects may allocate themselves. Null values retain the existing reader behavior and skip custom parsers for nullable/reference typed reads. Arrays use the registered parser for each non-null element.

Use an explicitly typed span lambda or a `ParseSpan<T>` variable to select the span overload. For a method group, assign or cast it to `ParseSpan<T>` first:

```csharp
ParseSpan<int> parse = int.Parse;
var parsers = new RuntimeParsersDescriptorBuilder().WithParser(parse).Build();
```

Existing string delegates continue to work. With C# 13 or newer, overload priorities keep untyped lambdas and overloaded method groups on the legacy string overload. With an older compiler, explicitly type the lambda parameter as `string` when overload resolution is ambiguous.

To compare custom numeric and date parsers at field and runtime scope, run:

```text
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*CustomParsing*' --job short
```

These benchmarks measure steady-state typed access with registration and initial parser caching outside the measured operation. `MemoryDiagnoser` reports total managed allocations alongside throughput, including intermediate strings and any boxing performed by the legacy adapter. They do not measure CSV tokenization or boxed access. The custom benchmark runner preserves the current power plan.
