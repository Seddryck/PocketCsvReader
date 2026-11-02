---
title: Span mapper and object builder
tags: [configuration]
---

Use `CsvReader.To<T>` to turn CSV rows into application objects. A `SpanMapper<T>` decides how each row becomes an object; `SpanObjectBuilder<T>` provides a mapper that parses fields and passes them to a public constructor of `T`.

## Example: import customer orders

Suppose an order export contains an order number, customer name, order date, total, and payment status:

```csv
OrderId,Customer,OrderedOn,Total,Status
1001,Ada Lovelace,2026-10-30,149.95,paid
1002,Grace Hopper,2026-10-31,89.50,pending
```

The following complete console example reads those orders from an in-memory UTF-8 stream. It overrides the date parser to require ISO dates and registers a parser for the application's payment-status enum.

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

The CSV column order must match the constructor parameter order. A class with only a parameterless constructor and settable properties will not work for a row containing fields. Use a custom mapper when you need to select columns, reorder values, or assign properties.

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
