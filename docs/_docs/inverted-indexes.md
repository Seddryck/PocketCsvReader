---
title: Use inverted indexes
tags: [usage]
---

An inverted index applies an extractor to a parsed CSV field and records which rows contain each term. `ContainsAny` unions the postings for the requested terms. `ContainsAll` intersects them. The reader seeks to matching rows in source order and reads each row once, even if it contains repeated terms or matches several requested terms.

## Build and query an inverted index

Add `PocketCsvReader.Indexing`. Define the field by header name or zero-based source ordinal.

```csharp
using PocketCsvReader.Configuration;
using PocketCsvReader.Indexing;

var orders = new CsvReaderBuilder()
    .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
    .FromFiles(new[] { "orders-2026-05.csv", "orders-2026-06.csv" })
    .WithPartitions(p => p.FromFileName("orders-{month}.csv"))
    .Build();

var tags = new InvertedIndex(
    new IndexKey<string>("Tags", comparison: StringComparison.OrdinalIgnoreCase),
    ExtractTags);
orders.BuildIndexes(tags);

var predicate = TermPredicate.ContainsAll("urgent", "refund");
using var rows = orders.WithIndexedLookup(tags, predicate).ToDataReader();
while (rows.Read())
    Console.WriteLine(rows["Tags"]);

static string[] ExtractTags(string value)
    => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
```

Use `InvertedIndex.Create<TField, TTerm>` for other supported key types or to extract a different term type. For example, `InvertedIndex.Create<DateOnly, int>(new IndexKey<DateOnly>("Date"), static date => [date.Year, date.Month])` indexes integer terms from a parsed date. The field comparison is used by default when the two types match. Use `termComparison` to override string term comparison, with an optional `termCulture` for culture-sensitive comparison. Use `normalizeTerm` to apply the same normalization to both extracted and requested terms.

Lookup terms must have the configured term type. An empty request matches zero rows. Missing and null fields contribute no terms. The extractor returns a sequence, which can be empty. Null elements in the sequence are ignored, and a normalizer must return a non-null term. For a plain `CsvReader`, use `csv.BuildIndexes(path, tags)` and `csv.WithIndexedLookup(path, tags, predicate)`.

## Key and sidecar rules

Inverted indexes share the [Sparse key rules](../sparse-indexes/), including registered runtime parsers, explicit formats and cultures, and string comparison. An extractor, normalizer, or runtime parser used by a persisted index must be a deterministic static method or non-capturing lambda. Lookup validates the sidecar version, checksum, source stamps, and compatibility with the field, term rules, and reader settings.

`BuildIndexes` scans every data record in each selected file, regardless of row filters, skip, or take. It replaces the versioned `.inverted.idx` sidecar only if the source byte length and UTC modification time stay unchanged. Partition filters run before sidecar checks. Row filters and pagination apply after term selection.

By default each selected file requires a usable sidecar. `WithOptionalIndex()` allows a sequential scan when a sidecar is missing or the reader configuration cannot use one. A stale, unreadable, corrupt, or incompatible sidecar for a supported source fails under either policy. Lookup never creates or rebuilds a sidecar. `WithRequiredIndex()` restores strict lookup.

Inverted indexes support local, seekable, uncompressed UTF-8 CSV with ASCII syntax and an optional BOM. When headers are enabled, each file needs its own header. Numbered comment rows, array fields, leading row counts, custom stream openers, and bad-data policies require optional scans. An already-open `IDataReader` also requires an optional index. The returned reader uses the definition's key settings and owns the input.

The sidecar stores 16 bytes per distinct term-to-row posting plus term metadata. Lookup reads the sidecar to verify its checksum before combining the requested postings. A missing required term in a `ContainsAll` lookup yields an empty result without parsing CSV data records.
