---
title: Sequences' replacement
tags: [configuration]
---
In the context of CSV parsing or text processing, sequences are specific strings or patterns that are replaced by predefined values during the parsing process. These sequences are used to standardize or interpret certain textual representations in the data, making it easier to process and analyze.

For example, a sequence could be a placeholder for a null value, a replacement for a long or repetitive string, or a shorthand for a more complex value.

## Common Use Cases for Sequences

### Null Sequence

- Description: A string that represents null values in the CSV file.
- Example: A CSV file might use the word "NULL" to indicate missing data. During parsing, the sequence "NULL" is replaced with null in the resulting data structure.

```plaintext
Name,Age
Alice,25
Bob,NULL
```

After parsing with a null sequence defined as `NULL`, the data would look like:

```json
[
    { "Name": "Alice", "Age": 25 },
    { "Name": "Bob", "Age": null }
]
```

### Placeholder Replacement

- Description: Replace a short placeholder string with a more meaningful or longer value.
- Example: The CSV might use "N/A" for "Not Applicable," which could be replaced with a more descriptive value like "Not Available" during parsing.

```plaintext
Product,Status
Widget,N/A
Gadget,In Stock
```

After parsing with a replacement for  `N/A`, the data would look like:

```json
[
    { "Product": "Widget", "Status": "Not Available" },
    { "Product": "Gadget", "Status": "In Stock" }
]
```

### Compression of Repeated Values

- Description: Use a shorthand sequence to represent a long, repeated value to save space in the file.
- Example: Instead of repeating the full string "Available in Store" multiple times, the file might use "AIS", which gets expanded during parsing.

```plaintext
Product,Availability
Widget,AIS
Gadget,AIS
```

With a sequence defined for `AIS`, the data would look like:

```json
[
    { "Product": "Widget", "Availability": "Available in Store" },
    { "Product": "Gadget", "Availability": "Available in Store" }
]
```

## Implementing Sequences in a Parser

Define the sequences in the parser configuration, such as:

```csharp
var profile = new CsvProfile()

var options = new ParserOptimizationOptions
{
    HandleSpecialValues = true,
};

profile.ParserOptimizations = options;

profile.Sequences.Add("AIS", "Available in Store");
profile.Sequences.Add("-", 0);
```

This configuration will apply these sequences during parsing to replace or interpret the specified strings.