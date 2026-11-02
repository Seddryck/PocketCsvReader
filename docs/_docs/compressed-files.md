---
title: Read compressed files
tags: [configuration]
---

Use resource configuration to read GZip, Deflate, Brotli, or ZIP input through the usual `CsvReader` methods. This page covers compression configuration and archive behavior. See [Fluent API for Resource Configuration](../fluent-api-resource/) for encoding and value substitution settings.

## Specifying the Compression to the Reader

You can specify compression using the `ResourceDescriptorBuilder` class within a `CsvProfile`. The `WithCompression` method accepts a compression name either as a string or as a `CompressionFormat` enumeration value. Alternatively, call `WithoutCompression` to indicate that the file is not compressed — this is also the default.

```csharp
using PocketCsvReader;
using PocketCsvReader.Configuration;

var profile = new CsvProfile(
    new DialectDescriptorBuilder()
        .WithDelimiter(',')
        .WithLineTerminator("\n")
        .WithoutHeader()
        .Build(),
    null,
    new ResourceDescriptorBuilder()
        .WithEncoding("utf-8")
        .WithCompression("gz")
        .Build());

var csvReader = new CsvReader(profile);
using var reader = csvReader.ToDataReader("orders.csv.gz");
while (reader.Read())
{
    Console.WriteLine(reader.GetString(0));
}
```

Set `WithEncoding` to the encoding of the decompressed text. Compression must be configured explicitly; the reader does not infer it from the filename extension. The example has no header; replace `.WithoutHeader()` with `.WithHeader()` when the first row contains column names.

For a Brotli-compressed CSV, configure the resource with either string identifier or the typed format:

```csharp
new ResourceDescriptorBuilder().WithEncoding("utf-8").WithCompression("brotli").Build();
new ResourceDescriptorBuilder().WithEncoding("utf-8").WithCompression("br").Build();
new ResourceDescriptorBuilder().WithEncoding("utf-8").WithCompression(CompressionFormat.Brotli).Build();
```

Pass this resource descriptor to `CsvProfile` as in the example above, then read the compressed file or stream through `CsvReader`.

### Supported Compressions

The library supports the following formats:

- `deflate`, with aliases `zz` and `def`: [RFC 1951](https://datatracker.ietf.org/doc/html/rfc1951)
- `gz` (GZip), with alias `gzip`: [RFC 1952](https://datatracker.ietf.org/doc/html/rfc1952)
- `brotli`, with alias `br`
- `zip`, with alias `zipfile`

Identifiers are case-insensitive and may start with a dot, for example `.BR`.

- `GetSupportedKeys()` returns all known aliases.
- `GetSupportedCompressions()` returns the four canonical identifiers: `gz`, `deflate`, `brotli`, and `zip`.
- `GetCompression(string alias)` returns the primary name for a given alias.

## Internal Classes

### Class DecompressorFactory

The `PocketCsvReader.Compression` namespace also exposes `DecompressorFactory` for direct decompression. `Streaming` returns a forward-only decompression stream. `Buffered` decompresses into a seekable in-memory stream, allowing callers to rewind or probe the decompressed data. `CsvReader` chooses the decompression strategy during stream initialization; the fluent resource API configures the format, rather than selecting a factory mode.

For Brotli, streaming decompresses from the input's current position without seeking. Buffered decompression rewinds seekable input and returns a seekable in-memory stream positioned at the beginning; non-seekable input is read from its current position. Dispose the returned stream when finished. Both modes leave the compressed input open for the caller to dispose.

### Single File vs Archive

Formats like GZip, Deflate, and Brotli compress one stream to another — effectively one file into one file. Brotli is not a multi-file archive. In contrast, an archive contains metadata and multiple file streams.

When using an archive (e.g., with `ZipDecompressor`), the implementation assumes the archive contains a single file. If it doesn't, decompression fails. This constraint exists because the library is focused on delimited (CSV-like) files only.
