---
title: Basic usage of the library
tags: [quick-start]
---
The `CsvReader` class is a flexible and efficient tool for reading and parsing CSV files or streams into various formats, such as `DataTable`, `IDataReader`, or strongly-typed objects. This documentation explains the basics of how to use the class, including common use cases and examples.

## Features

- Read CSV files or streams into a `DataTable`.
- Access CSV data in a forward-only, read-only manner using `IDataReader`.
- Map CSV records to strongly-typed objects.
- Map CSV records to array of strings.
- Customizable CSV parsing profiles for delimiters, quote handling, and more.
- Supports encoding detection through the `IEncodingDetector` interface.

## Getting Started

### Initialization

You can create an instance of `CsvReader` with various configurations:

```csharp
// Default configuration: comma-delimited, double quotes for escaping, 4 KB buffer size.
var csvReader = new CsvReader();

// Custom CSV profile (e.g., semicolon-delimited, double quotes for escaping).
var csvReaderWithProfile = new CsvReader(CsvProfile.SemiColumnDoubleQuote);

// Custom buffer size for large files.
var csvReaderWithBuffer = new CsvReader(bufferSize: 64 * 1024);

// Both custom profile and buffer size.
var csvReaderCustom = new CsvReader(CsvProfile.SemiColumnDoubleQuote, bufferSize: 16 * 1024);
```

---

## Reading CSV Data

### 1. Reading Into a `DataTable`

The `ToDataTable` method reads CSV data and returns a `DataTable` containing all rows and fields.

#### From a File to a DataTable

```csharp
DataTable dataTable = csvReader.ToDataTable("example.csv");
```

#### From a Stream to a DataTable

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
DataTable dataTable = csvReader.ToDataTable(stream);
```

---

### 2. Accessing Data with `IDataReader`

The `ToDataReader` method provides a forward-only, read-only `IDataReader` for processing large files efficiently.

#### From a File to a IDataReader

```csharp
using var reader = csvReader.ToDataReader("example.csv");
while (reader.Read())
{
    Console.WriteLine(reader[0]); // Access the first column of the current row.
}
```

#### From a Stream to a IDataReader

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
using var reader = csvReader.ToDataReader(stream);
while (reader.Read())
{
    Console.WriteLine(reader[0]); // Access the first column of the current row.
}
```

---

### 3. Reading as Arrays

The `ToArrayString` method returns an enumerable of string arrays, where each array represents a record (row) of fields.

#### From a File to an array

```csharp
foreach (var record in csvReader.ToArrayString("example.csv"))
{
    Console.WriteLine(string.Join(", ", record));
}
```

#### From a Stream to an array

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
foreach (var record in csvReader.ToArrayString(stream))
{
    Console.WriteLine(string.Join(", ", record));
}
```

---

### 4. Mapping Records to Strongly-Typed Objects

The `To<T>` method maps CSV records to objects of a specified type.

#### Example: Mapping to a `Person` Class

```csharp
public class Person
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
}

IEnumerable<Person> people = csvReader.To<Person>("example.csv");

foreach (var person in people)
{
    Console.WriteLine($"{person.FirstName} {person.LastName}, Age: {person.Age}");
}
```

#### From a Stream to Strongly-Typed Objects

```csharp
using var stream = new FileStream("example.csv", FileMode.Open, FileAccess.Read);
IEnumerable<Person> people = csvReader.To<Person>(stream);

foreach (var person in people)
{
    Console.WriteLine($"{person.FirstName} {person.LastName}, Age: {person.Age}");
}
```

## Error Handling

The following exceptions may be thrown:

- `FileNotFoundException`: If the specified file does not exist.
- `ArgumentNullException`: If a required argument (e.g., stream) is null.
- `IOException`: For general file or stream-related errors.

Ensure proper exception handling to avoid runtime issues:

```csharp
try
{
    var data = csvReader.ToDataTable("example.csv");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"File not found: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}
```

## Best Practices and Additional Notes

1. **Use Streams for Large Files:** Use stream-based methods to avoid loading large files entirely into memory.
2. **Dispose Resources:** Always dispose of `IDataReader` and `Stream` objects after use.
3. The class uses an `IEncodingDetector` to automatically detect the file encoding. You can replace this with a custom implementation if needed.
4. Buffer size configuration can improve performance for specific use cases.
