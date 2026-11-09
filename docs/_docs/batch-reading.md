---
title: Read multiple files and number rows
tags: [usage]
---

CSV can expose several inputs as one forward-only `CsvBatchDataReader`. Supply filenames, already-open streams, or functions that open streams. Stream factories avoid opening every file up front.

## Example with stream factories

<!-- example: batch-reader -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .WithRowNumber()
    .Build();
Func<Stream>[] inputs =
[
    () => new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n")),
    () => new MemoryStream(Encoding.UTF8.GetBytes("Name\nGrace\n"))
];
using var reader = csv.ToDataReader(inputs);
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
```

<!-- output: batch-reader -->
```text
1: Ada
2: Grace
```

For files, call `csv.ToDataReader(new[] { "part1.csv", "part2.csv" })`. `ToDbDataReader` has corresponding batch overloads. These overloads call synchronous factories even when rows are read with `ReadAsync(token)`.

## Asynchronous stream factories

Pass an `IEnumerable<Func<CancellationToken, ValueTask<Stream>>>` to `ToDataReader`, `ToDbDataReader`, or `CsvBatchDataReader.FromAsyncStreamFactories` when opening an input requires asynchronous I/O. The first `ReadAsync` opens the first input. Later factories are awaited in order as needed, after the previous input has been disposed. An empty factory sequence returns no rows.

```csharp
using System.Net.Http;
using PocketCsvReader.Configuration;

static async Task PrintRemoteCsvAsync(HttpClient http, IEnumerable<Uri> uris, CancellationToken token)
{
    var csv = new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
        .Build();
    var openers = uris.Select(uri =>
        (Func<CancellationToken, ValueTask<Stream>>)(async ct => await http.GetStreamAsync(uri, ct)));

    await using var reader = csv.ToDataReader(openers);
    while (await reader.ReadAsync(token))
        Console.WriteLine(reader.GetString(0));
}
```

The caller owns the `HttpClient` and keeps it alive until the batch is disposed. `GetStreamAsync` provides the response body stream directly and the batch takes ownership of that stream. [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.getstreamasync?view=net-10.0)

Factories receive the active read token and should pass it to their I/O. Return a readable stream positioned at the start of the CSV. A factory owns any resources it allocates until it returns the stream. If a factory uses `SendAsync` directly, it must dispose the response on failure and arrange for disposing the returned stream to release the response on success.

A read that starts with a cancelled token leaves the batch available for another read. Cancellation or failure during acquisition or record reading closes the batch and disposes its active stream. Dispose the reader after the active read finishes. Use `await using` to allow asynchronous stream cleanup. Unused factories are never invoked during disposal.

Synchronous `Read()` throws `NotSupportedException`. The database adapter's `HasRows` also throws until the first completed `ReadAsync` determines whether the batch contains rows. After that it returns the cached value.

## Headers and schema

By default, each file has its own header (`HeaderRepeat = true`). Use `WithHeaderRepeat(false)` when only the first file supplies a header; the batch carries those names forward. All inputs use the same profile. Keep column order, conversion schema, and header expectations compatible across files.

## Ownership

Filename, stream, and synchronous-factory batches open their first input during construction. Advancing closes the previous stream. Disposing closes the current stream and releases the batch enumerator. The batch owns provided streams and streams returned by factories. Prefer factories for many files and dispose a batch even if no records are read. Do not reuse a consumed stream sequence without resetting/reopening it.

## Generated row numbers

`WithRowNumber()` prepends an `int` column named `rowNumber`; `WithRowNumber("recordId")` chooses its name, and `WithoutRowNumber()` disables it. The last call wins. Null, empty, or whitespace names are rejected. A name exactly colliding with a source column throws `InvalidDataException`.

Numbers start at one for each reader and continue across batch inputs. Headers, comments, and records skipped by malformed-data recovery do not consume a generated row number. [Reader filters](../reader-filtering/) run after numbering, so filtered-out data records consume a number and leave gaps. Multiline CSV records receive one number. Overflow beyond `Int32` throws.

Constructor mapping requires an additional first parameter for the generated value; custom CSV span mappers receive its field first. Tables and parsed row outputs expose an integer; string arrays expose a decimal string. Source schema indexes remain indexes of the original columns. Generated row numbers differ from [source positions](../source-positions/), which identify source records and offsets and can count skipped malformed records differently.

Use [partitioned CSV queries](../file-partitions/) to discover metadata and defer opening the first file until a row request.
