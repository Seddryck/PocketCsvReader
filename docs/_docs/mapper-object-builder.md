---
title: Map rows to objects
tags: [configuration]
---

Use `CsvReader.To<T>` to turn CSV rows into application objects. Choose `ObjectMap<T>` for named columns and writable properties, `SpanObjectBuilder<T>` for positional constructor mapping, or `SpanMapper<T>` for a custom projection over raw field spans.

## Choose a mapper

| Mapper | Binding and conversion | Use it when |
| --- | --- | --- |
| `ObjectMap<T>` | Named headers, typed reader getters, writable properties | Columns can move or include extra fields, reader parsers and formats should apply, or a later CSV enriches existing objects |
| `SpanObjectBuilder<T>` | Fields in column order, public constructor parameters, builder-specific parsers | A record or constructor-based model matches the CSV layout |
| `SpanMapper<T>` | Row character span and field positions, application-defined parsing | A custom projection needs direct control over field selection, conversion, or construction |

`SpanMapper<T>` is the delegate's API name. `SpanObjectBuilder<T>.Instantiate` can be assigned to that delegate. Both use the `csv.To<T>(filename, mapper)` instance method. `ObjectMap<T>` uses the `csv.To(filename, map)` extension in the `PocketCsvReader` namespace, and C# infers `T` from the map. An explicit `csv.To<Person>(filename, null)` continues to select the default constructor mapper.

## Map headers to properties

```csharp
using PocketCsvReader;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .Build();

var personMap = ObjectMap<Person>.Create(() => new Person())
    .Map("ID", p => p.Id)
    .Map("Full Name", p => p.Name);

IEnumerable<Person> people = csv.To("people.csv", personMap);
```

For this map, both `ID,Full Name` and `Full Name,Extra,ID` are valid layouts. Header matching is ordinal and case-sensitive. Only mapped headers must be unique. Each mapped member must select an instance property directly on `T` with an ordinary public `set` accessor. Configure the map before enumerating and reuse it across files. Header ordinals are resolved once per enumeration, so files can have different column orders.

Property values use `GetFieldValue<TProperty>`, including the reader's configured field/runtime parsers, formats, sanitization, and null handling. When supplying a schema, include all mapped properties and the lookup key in it. Reader filters and skip/take settings apply before object mapping.

Missing or ambiguous mapped headers throw `InvalidDataException` before a target is created or updated. Unmapped source columns are ignored. Each factory call supplies the target for one accepted row, then the mapped property setters run.

### Enrich the same people from a second CSV

Given these files, the contact rows can arrive in a different order and cover a subset of the people.

**people.csv**

```csv
ID,Full Name
101,Ada Lovelace
102,Grace Hopper
103,Katherine Johnson
```

**contact.csv**

```csv
ID,Email
102,grace@example.com
101,ada@example.com
```

Continue with the reader and `personMap` above, adding `using System.Linq` for the collection operations.

```csharp
var peopleById = csv.To("people.csv", personMap).ToDictionary(p => p.Id);

ObjectMap<Person> ContactMap(IReadOnlyDictionary<int, Person> peopleById) =>
    ObjectMap<Person>
        .Create("ID", (int id) => peopleById[id])
        .Map("Email", p => p.EmailAddress);

var contactPeople = csv.To("contact.csv", ContactMap(peopleById));
var updatedPeople = contactPeople.ToList();

Console.WriteLine(ReferenceEquals(updatedPeople[0], peopleById[102])); // True
Console.WriteLine(peopleById[102].EmailAddress); // grace@example.com

public sealed class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
}
```

`ObjectMap<T>.Create<TKey>(string keyHeader, Func<TKey, T> resolve)` binds the key header once and reads each key through `GetFieldValue<TKey>`. The resolver returns the target, then the mapped properties are assigned. `ID` serves as the lookup key. Include `.Map("ID", p => p.Id)` if assigning the key property is also intended.

Enumeration applies the updates and yields the same resolved instance for each contact row in file order. A repeated ID yields and updates the same object again. `peopleById.Values` contains everyone from the first file, including Katherine with her original property values. The dictionary indexer throws `KeyNotFoundException` for an unknown ID. Resolver, conversion, and setter exceptions propagate during enumeration. Updates already applied remain on the objects when a later row fails.

The filename extension opens the input on enumeration and disposes it on completion, failure, or early enumerator disposal. The extension accepts a filename. The existing `SpanMapper<T>` overloads also accept streams and support asynchronous reading through `ToAsync<T>`.

## Example: import customer orders

Suppose an order export contains an order number, customer name, order date, total, and payment status:

```csv
OrderId,Customer,OrderedOn,Total,Status
1001,Ada Lovelace,2026-10-30,149.95,paid
1002,Grace Hopper,2026-10-31,89.50,pending
```

The following complete console example reads those orders from an in-memory UTF-8 stream. It overrides the date parser to require ISO dates and registers a parser for the application's payment-status enum.

<!-- example: order-import -->
```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;
using PocketCsvReader;

var csv = "OrderId,Customer,OrderedOn,Total,Status\n"
        + "1001,Ada Lovelace,2026-10-30,149.95,paid\n"
        + "1002,Grace Hopper,2026-10-31,89.50,pending\n";

var builder = new SpanObjectBuilder<Order>();
builder.SetParser<DateOnly>(value =>
    DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture));
builder.SetParser<PaymentStatus>(value =>
    Enum.Parse<PaymentStatus>(value, ignoreCase: true));

// Create and configure the builder once, then reuse it for every row.
SpanMapper<Order> mapper = builder.Instantiate;
var profile = new CsvProfile(',', '"', "\n", firstRowHeader: true);
var reader = new CsvReader(profile);

using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
foreach (var order in reader.To<Order>(stream, mapper))
{
    Console.WriteLine(FormattableString.Invariant(
        $"{order.OrderId}: {order.Customer}, {order.OrderedOn:yyyy-MM-dd}, {order.Total:F2}, {order.Status}"));
}

public record Order(
    int OrderId,
    string Customer,
    DateOnly OrderedOn,
    decimal Total,
    PaymentStatus Status);

public enum PaymentStatus { Pending, Paid }
```

Output:

<!-- output: order-import -->
```text
1001: Ada Lovelace, 2026-10-30, 149.95, Paid
1002: Grace Hopper, 2026-10-31, 89.50, Pending
```

To read the same export from disk, replace `reader.To<Order>(stream, mapper)` with `reader.To<Order>("orders.csv", mapper)`.

`To<T>` reads lazily: rows are parsed as you enumerate the result. Keep a supplied stream open until enumeration finishes. The header is skipped because `firstRowHeader` is `true`; it does not bind column names to properties or constructor parameters.

## How SpanObjectBuilder maps fields

```csharp
public T Instantiate(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans)
```

The builder selects the first public instance constructor whose parameter count equals the number of fields. It maps fields to constructor parameters **in column order**, looks up a parser for each parameter type, and invokes the constructor with the parsed values. Give the target type one unambiguous matching constructor, as the `Order` record does above.

The CSV column order must match the constructor parameter order. A class with only a parameterless constructor and settable properties requires an `ObjectMap<T>` or a custom `SpanMapper<T>`. Choose `ObjectMap<T>` to bind named headers to those properties, or a custom `SpanMapper<T>` to select and parse raw slices explicitly.

When every parameter uses a default parser, you can omit the mapper: `reader.To<YourType>(stream)` creates a `SpanObjectBuilder<YourType>` automatically. The order example needs its configured mapper because enums have no default parser.

### Default and custom parsers

The builder supplies parsers for:

- `string` and `char`.
- `int`, `long`, `short`, `byte`, `float`, `double`, and `decimal`.
- `bool` (`true` or `false`).
- `DateTime`, `DateOnly`, `TimeOnly`, and `DateTimeOffset`.

Numeric parsers use `CultureInfo.InvariantCulture`. Default date and time parsers use the current culture; register an explicit parser when the input has a fixed format, as in the order example. Nullable types, enums, and other types such as `Guid` require a registered parser.

```csharp
public void SetParser<TField>(Parse parse)
public delegate object? Parse(ReadOnlySpan<char> span);
```

`SetParser<TField>` adds or replaces the parser for a type on that builder. It affects every constructor parameter of that type. The delegate must return a value compatible with `TField`.

If no constructor has the required parameter count, constructor selection throws `InvalidOperationException`. An unregistered parameter type causes an exception stating that no parser was found. A parser failure is wrapped in a `FormatException` containing the zero-based field index, expected type, and input value; the original exception is available as `InnerException`.

## Write a SpanMapper for a projection

```csharp
public delegate T SpanMapper<T>(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans);
```

For an accounting summary, you might need only the order number and total from the same five-column export. Add the following code after the import loop above, placing the `OrderSummary` record alongside the other type declarations:

```csharp
SpanMapper<OrderSummary> summaryMapper = (span, fieldSpans) =>
{
    var fields = System.Linq.Enumerable.ToArray(fieldSpans);
    var id = fields[0].Value;
    var total = fields[3].Value;

    return new OrderSummary(
        int.Parse(span.Slice(id.Start, id.Length), CultureInfo.InvariantCulture),
        decimal.Parse(span.Slice(total.Start, total.Length), CultureInfo.InvariantCulture));
};

using var summaryStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
foreach (var summary in reader.To<OrderSummary>(summaryStream, summaryMapper))
{
    Console.WriteLine(FormattableString.Invariant($"{summary.OrderId}: {summary.Total:F2}"));
}

// Declare alongside the other types, after the top-level statements.
public record OrderSummary(int OrderId, decimal Total);
```

The mapper receives the current row's character span and field positions. Use `field.Value.Start` and `field.Value.Length` to slice a value. Parse or copy any values you need inside the callback; the row buffer is reused as reading advances. This projection produces `(1001, 149.95)` and `(1002, 89.50)` without parsing the date or payment status.

A custom `SpanMapper<T>` controls its own parsing of these slices, including escaped CSV text and special-value handling. Configure constructor conversion through `SpanObjectBuilder.SetParser`. Configure `ObjectMap<T>` conversion through the reader's schema and runtime parsers.

## Call Instantiate directly

Normally the reader supplies field positions. For a small, already-delimited row, you can also pass them yourself:

```csharp
var builder = new SpanObjectBuilder<StockItem>();
var fields = new[] { new FieldSpan(0, 2), new FieldSpan(3, 4) };
var item = builder.Instantiate("42,true".AsSpan(), fields);
Console.WriteLine(item); // StockItem { Quantity = 42, InStock = True }

public record StockItem(int Quantity, bool InStock);
```

`Instantiate` parses the supplied slices; it does not discover delimiters or split the row itself.
