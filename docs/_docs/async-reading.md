---
title: Read asynchronously and cancel
tags: [usage]
---

Use async advancement when input I/O should not block the calling thread. Typed getters remain synchronous: after `ReadAsync` completes, the current record is available in memory. Never overlap `Read` and `ReadAsync` on one reader.

## Standard DbDataReader

<!-- example: async-reader -->
```csharp
using System.Text;
using PocketCsvReader.Configuration;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .Build();
using var cancellation = new CancellationTokenSource();
await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n"));
await using var reader = csv.ToDbDataReader(stream);
while (await reader.ReadAsync(cancellation.Token))
    Console.WriteLine(reader.GetString(1));
```

<!-- output: async-reader -->
```text
Ada
```

The shared adapter delegates to the existing format parser and schema. Concrete readers also expose asynchronous advancement. Async reads forward cancellation to input I/O; cancellation does not make a partially interrupted reader restartable. Open a fresh reader/input to restart safely.

## Async typed CSV objects

`CsvReader.ToAsync<T>` exposes `IAsyncEnumerable<T>`. It uses the CSV span mapper or constructor builder and does not buffer the complete input. Pass `cancellationToken:` when calling it, or use the sequence's cancellation support during enumeration. File opening occurs when enumeration starts; filenames are closed on completion, failure, cancellation, or early disposal. Caller-provided streams remain open for this API.

Extension readers offer async data-reader access; do not assume every format exposes CSV's `ToAsync<T>` convenience method. Use the adapter and map the current row explicitly where needed.

## Disposal and prefetch

Use `await using` for an async-disposable adapter. `CloseAsync`/`DisposeAsync` release its input. `HasRows` may prefetch the first row; the next successful advancement still returns it. Getters do not perform concurrent input reads.

See the authoritative [stream lifetime table](../stream-lifetimes/), [batch reading](../batch-reading/), and [source positions](../source-positions/) for ownership and metadata.
