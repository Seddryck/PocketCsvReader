---
title: Read compressed files
tags: [configuration]
---

The `PocketCsvReader.Compression` namespace provides `DecompressorFactory` and multiple classes implementing the `IDecompressor` interface. These classes support decompressing files using `GZip`, `Deflate`, and `Brotli` formats. The class handling `Zip` supports `deflate` compression, but also accounts for the fact that a ZIP archive may contain multiple files.

## Specifying the Compression to the Reader

You can specify compression using the `ResourceDescriptorBuilder` class within a `CsvProfile`. The `WithCompression` method accepts a compression name either as a string or as a `CompressionFormat` enumeration value. Alternatively, call `WithoutCompression` to indicate that the file is not compressed — this is also the default.

```csharp
var profile = new CsvProfile(
    new DialectDescriptorBuilder()
        .WithDelimiter(',')
        .WithLineTerminator("\n")
        .Build(),
    null,
    new ResourceDescriptorBuilder()
        .WithEncoding("utf-8")
        .WithCompression("gz")
        .Build());
```

When using compression, it's strongly recommended to specify the encoding of the underlying file. This significantly improves memory efficiency. See below for more context.

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

The `DecompressorFactory` offers two instantiation options: `Streaming` and `Buffered`. For better memory performance, prefer `Streaming`, but note that it's forward-only, meaning you cannot detect encoding. If encoding cannot be predefined, use the `Buffered` version instead.

For Brotli, streaming decompresses from the input's current position without seeking. Buffered decompression rewinds seekable input and returns a seekable in-memory stream positioned at the beginning; non-seekable input is read from its current position. Dispose the returned stream when finished. Both modes leave the compressed input open for the caller to dispose.

### Single File vs Archive

Formats like GZip, Deflate, and Brotli compress one stream to another — effectively one file into one file. Brotli is not a multi-file archive. In contrast, an archive contains metadata and multiple file streams.

When using an archive (e.g., with `ZipDecompressor`), the implementation assumes the archive contains a single file. If it doesn't, decompression fails. This constraint exists because the library is focused on delimited (CSV-like) files only.
