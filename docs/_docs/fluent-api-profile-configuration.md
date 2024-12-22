---
title: Fluent API for profile configuration
tags: [configuration]
---
The `PocketCsvReader.Configuration` namespace provides `CsvReaderBuilder` and `DialectDescriptorBuilder` classes to configure and create a CSV reader for processing CSV files. These classes offer a fluent API to customize the behavior of a CSV reader, including delimiter characters, line terminators, quoting behavior, and more.

## Class `CsvReaderBuilder`

The `CsvReaderBuilder` class provides a fluent interface to configure and create a `CsvReader` instance using a custom `CsvDialectDescriptor`.

### Dialect descriptor

The `CsvReaderBuilder` allows you to specify a `DialectDescriptor` using the `WithDialectDescriptor` method and then build a `CsvReader` using the `Build` method.

### Usage of `CsvReaderBuilder`

1. **Initialization:**

   ```csharp
   var csvReaderBuilder = new CsvReaderBuilder();
   ```

2. **Configuring the Descriptor:**
   Use the `WithDialectDescriptor` method to provide a configuration function that modifies the `DialectDescriptorBuilder`.

   ```csharp
   var csvReader = new CsvReaderBuilder()
       .WithDialectDescriptor(builder => builder
           .WithDelimiter(';')
           .WithLineTerminator("\r\n")
           .WithQuoteChar('"')
           .WithHeader(true))
       .Build();
   ```

3. **Building the CSV Reader:**
   Call the `Build` method to create a `CsvReader` instance configured with the specified dialect.

   ```csharp
   var csvReader = csvReaderBuilder.Build();
   ```

## Class `DialectDescriptorBuilder`

The `DialectDescriptorBuilder` class allows you to configure a CSV dialect descriptor that defines the structure and rules for parsing CSV files.

### Usage of `DialectDescriptorBuilder`

1. **Initialization:**

   ```csharp
   var builder = new DialectDescriptorBuilder();
   ```

2. **Method Chaining to Configure the Descriptor:**

   ```csharp
   var descriptor = new DialectDescriptorBuilder()
       .WithDelimiter(';')
       .WithLineTerminator("\r\n")
       .WithQuoteChar('"')
       .WithDoubleQuote(true)
       .WithEscapeChar('\\')
       .WithNullSequence("NULL")
       .WithSkipInitialSpace(true)
       .WithHeader(true)
       .Build();
   ```

3. **Build the Descriptor:**
   Call the `Build` method to retrieve the configured `CsvDialectDescriptor` object:

   ```csharp
   var descriptor = builder.Build();
   ```

### Available Methods of `DialectDescriptorBuilder`

| **Method**                          | **Description**                                                                                  |
|-------------------------------------|--------------------------------------------------------------------------------------------------|
| `WithDelimiter(char delimiter)`     | Sets the delimiter character used in the CSV.                                                    |
| `WithLineTerminator(string line)`   | Sets the line terminator (e.g., `"\r\n"` for Windows or `"\n"` for Unix).                        |
| `WithQuoteChar(char quoteChar)`     | Sets the character used for quoting fields.                                                      |
| `WithoutQuoteChar()`   | Unsets the quote character used in the CSV. |
| `WithDoubleQuote(bool doubleQuote)` | Enables or disables double quoting for fields containing special characters.                     |
| `WithoutDoubleQuote()`              | Disables double quoting (same as calling `WithDoubleQuote(false)`).                              |
| `WithEscapeChar(char escapeChar)`   | Sets the escape character used in the CSV.                                                       |
| `WithoutEscapeChar()`   | Unsets the escape character used in the CSV. |
| `WithNullSequence(string? nullSeq)` | Defines a sequence used to represent `null` values in the CSV.                                   |
| `WithoutNullSequence()`             | Removes the null sequence (same as calling `WithNullSequence(null)`).                            |
| `WithSkipInitialSpace(bool skip)`   | Enables or disables skipping spaces after the delimiter.                                         |
| `WithoutSkipInitialSpace()`         | Disables skipping spaces (same as calling `WithSkipInitialSpace(false)`).                        |
| `WithHeader(bool header)`           | Enables or disables the inclusion of a header row.                                               |
| `WithoutHeader()`                   | Disables headers (same as calling `WithHeader(false)`).                                          |
| `WithHeaderRows(int[] rows)`        | Enables headers and set the indexes of header rows.                                          |
| `WithHeaderJoin(string join)`       | Set the string to join fields from different rows to create the header.                                        |
| `WithCommentChar(char commentChar)` | Sets the character used to denote comments in the CSV. |
| `WithCommentRows(int[] rows)` | Set the indexes of comment rows.                                           |
| `WithCaseSensitiveHeader(bool cs)`  | Enables or disables case sensitivity for header fields.                                          |
| `WithCsvDdfVersion(string version)` | Sets the version of the CSV DDF (Data Definition Format).                                        |
