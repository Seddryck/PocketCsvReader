---
title: Span mapper and object builder
tags: [configuration]
---

This documentation explains how to use the `SpanMapper<T>` and `SpanObjectBuilder<T>` classes for mapping and parsing flat-file data. Their primary purpose is to configure the mapping of fields from delimited data to a strongly-typed class.

## Delegates

### SpanMapper&lt;T&gt;

```csharp
public delegate T SpanMapper<T>(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans);
```

**Purpose:**  
The `SpanMapper<T>` delegate maps data from a `ReadOnlySpan<char>` representing a row of delimited flat-file data into a strongly-typed object of type `T`. It uses a collection of `FieldSpan` objects to determine the start and length of each field in the span.

- **`span`**: The source `ReadOnlySpan<char>` containing the delimited row data.
- **`fieldSpans`**: A collection of `FieldSpan` objects defining the start position and length of each field in the row.

### Parse

```csharp
public delegate object? Parse(ReadOnlySpan<char> span);
```

**Purpose:**  
The `Parse` delegate defines a method for parsing a `ReadOnlySpan<char>` into an object of a specific type. It is used to handle custom parsing for various data types in the `SpanObjectBuilder<T>` class.

- **`span`**: The `ReadOnlySpan<char>` containing the value to parse.

## Class SpanObjectBuilder&lt;T&gt;

The `SpanObjectBuilder<T>` class is designed to instantiate strongly-typed objects (`T`) from delimited flat-file data using `SpanMapper<T>` and the `Parse` delegates. It supports default parsers for common data types and allows customization via the `SetParser` method.

### Default Parsers

By default, the `SpanObjectBuilder<T>` supports the following types:

- Strings
- Numbers (`int`, `long`, `short`, `byte`, `float`, `double`, `decimal`)
- Booleans
- Dates (`DateTime`, `DateOnly`, `TimeOnly`, `DateTimeOffset`)
- Characters (`char`)

### SetParser&lt;T&gt;TField&lt;T&gt;

If you need to parse additional types or override the default behavior, use the `SetParser` method.

**Purpose:**  
Customizes the parser for a specific data type.

- **`TField`**: The type for which the parser is being set.
- **`parse`**: A delegate implementing custom parsing logic for `TField`.

**Example:**

```csharp
var builder = new SpanObjectBuilder<MyClass>();
builder.SetParser<Guid>(s => Guid.Parse(s));
```

### `Instantiate`

```csharp
public T Instantiate(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans)
```

**Purpose:**  
Creates an instance of type `T` using constructor injection. The fields in the constructor are populated based on `fieldSpans` and the mapped parsers in `ParserMapping`.

- **`span`**: The `ReadOnlySpan<char>` containing the delimited row data.
- **`fieldSpans`**: A collection of `FieldSpan` objects specifying the position and length of each field.

**Behavior:**

1. Identifies the appropriate constructor of `T` by matching the number of fields in `fieldSpans`.
2. Iterates through each `FieldSpan`, using the associated parser to convert the field to the required type.
3. If a type lacks a parser, throws an exception.
4. If parsing fails, throws a `FormatException` with detailed error information.

**Example:**

```csharp
var builder = new SpanObjectBuilder<MyClass>();
var spans = new List<FieldSpan>
{
    new FieldSpan { Start = 0, Length = 5 },   // Field 1
    new FieldSpan { Start = 6, Length = 10 }  // Field 2
};
var obj = builder.Instantiate("12345     true", spans);
```

### To&lt;T&gt; Method

To integrate `SpanMapper<T>` and `SpanObjectBuilder<T>`:

1. Define a `SpanMapper<T>` that calls the `Instantiate` method of `SpanObjectBuilder<T>`.
2. Use the `To<T>` method to map rows of delimited data into strongly-typed objects.

**Example:**

```csharp
SpanMapper<MyClass> mapper = (span, fieldSpans) => 
    new SpanObjectBuilder<MyClass>().Instantiate(span, fieldSpans);

var result = mapper("12345     true".AsSpan(), fieldSpans);
```
