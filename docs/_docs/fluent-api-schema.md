---
title: Fluent API for Schema
tags: [configuration]
---

## Overview

The Fluent API for schema definition in PocketCsvReader provides an intuitive and expressive way to define the structure of CSV data. This is particularly useful when working with `IDataReader`, where the `GetValue` method returns a boxed `object`. This powerful feature enables dynamic retrieval of any column's value without prior type knowledge, making it highly flexible for handling various data types. It seamlessly integrates with schema definitions to ensure proper casting and minimize conversion overhead.

Defining a schema ensures that values are correctly interpreted and cast to their expected types, avoiding unnecessary type conversions at runtime.

## Defining a Schema

PocketCsvReader provides two ways to define schemas:

- Indexed Schema: Fields are defined by their position (index) in the dataset.
- Named Schema: Fields are defined by their column names.

### Creating an Indexed Schema

Indexed schemas are useful when working with CSV files that do not contain headers or when column order is fixed.

Example:

```csharp
var schema = new SchemaDescriptorBuilder()
    .Indexed()
    .WithField<int>()
    .WithField<string>(x => x.WithName("Description"))
    .Build();
```

In this example:

- The first column is an int.
- The second column is a string with the name "Description".

### Creating a Named Schema

Named schemas provide more flexibility when working with CSV files that contain headers.

Example:

```csharp
var schema = new SchemaDescriptorBuilder()
    .Named()
    .WithField<int>("ID")
    .WithField<string>("Description")
    .Build();
```

Here, the schema explicitly assigns types to fields based on column names.

## Using Field Formatting and Format Descriptors

The `WithFormat()` method allows specifying a format for fields that require special parsing, such as `DateTime` values, and relies on format descriptor builders like `IntegerFormatDescriptorBuilder`, `NumberFormatDescriptorBuilder`, and `TemporalFormatDescriptorBuilder` to handle culture-specific formatting details. This format is passed to the parser of the respective type, ensuring correct conversion from text to the expected type.

**Example:**

```csharp
var schema = new SchemaDescriptorBuilder()
    .Named()
    .WithField<DateTime>("Date", x => x.WithFormat("dd/MM/yyyy"))
    .Build();
```

In this example, the "Date" field is expected to be in the format `dd/MM/yyyy` (e.g., `25/12/2024`). The parser will use this format to correctly interpret and convert the string into a DateTime object.

Using `WithFormat()` ensures that structured data such as dates are properly parsed and prevents errors due to mismatched formats. The `TemporalFormatDescriptorBuilder` provides control over date and time separators, ensuring compatibility with different cultural representations.

### Numeric Formatting

The `NumericFieldDescriptorBuilder` allows further customization of numeric fields:

- `.WithDecimalChar(char decimalChar)`: Defines the character used for the decimal separator.
- `.WithGroupChar(char? groupChar)`: Defines the character used for digit grouping. Passing null removes grouping.
- `.WithoutGroupChar()`: Explicitly disables grouping.

Example:

```csharp
var schema = new SchemaDescriptorBuilder()
    .Named()
    .WithNumericField<double>("Amount", x => x.WithDecimalChar(',')
                                               .WithoutGroupChar())
    .Build();
```

This defines an "Amount" field as a double, using `,` as the decimal separator and disabling digit grouping.

### Custom Field

#### Defining a Custom Field with Formatting

You can define a custom field in your schema and specify how format should be interpreted when deserialized.

```csharp
var schema = new SchemaDescriptorBuilder()
    .Named()
    .WithCustomField<Point>("Location", x => x.WithFormat("x;y"))
    .Build();
```

In this example, the "Location" field is registered as a Point and formatted using the `x;y` pattern.

By default, when assigning a custom field, the system looks for a Parse method on the specified type that:

- Accepts a string (the input to parse).
- Has an optional string parameter for a custom format.
- Takes an IFormatProvider as the last argument.

If such a method exists, it is used automatically.

#### Providing a Custom Parser

If the default method resolution does not find a suitable Parse method, or if the required method is inaccessible, you can manually register a parser using `WithParser`:

```csharp
Point parse(string input)
{
    var parts = input.Split(';');
    return new Point(parts[0], parts[1]);
}

var schema = new SchemaDescriptorBuilder()
    .Named()
    .WithCustomField<Point>("Location", x => x.WithParser(parse))
    .Build();
```

Here, the custom parser method:

- Splits the input string using ; as a delimiter.
- Parses the resulting parts into Point coordinates.
- Returns a new Point instance.

This approach ensures that custom parsing logic is used when the default method resolution does not apply.

## Benefits of Using a Schema

- Ensures Type Safety: The schema guarantees that values are returned in their expected type.
- Simplifies Parsing: Eliminates the need for manual type conversion when using IDataReader.GetValue.
- Improves Readability: Fluent API provides a clean and declarative way to define schemas.
- Customizable Numeric Fields: Allows control over decimal and grouping characters for numeric fields.

## Conclusion

Using the Fluent API for schema definition in PocketCsvReader significantly enhances the usability and reliability of working with CSV data, especially when processing untyped data from an IDataReader. By leveraging indexed or named schemas, developers can streamline their data processing workflows while ensuring type safety and maintainability.
