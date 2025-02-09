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
