---
title: Read compressed files
tags: [configuration]
---

The `PocketCsvReader.Compression` namespace provides `DecompressorFactory` and multiple classes implementing the `IDecompressor` interface. These classes support decompressing files using `GZip` and `Deflate` formats. The class handling `Zip` supports `deflate` compression, but also accounts for the fact that a ZIP archive may contain multiple files.

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

### Supported Compressions

The library supports the following formats:

- `Deflate`, with aliases `zz` and `def`: [RFC 1951](https://datatracker.ietf.org/doc/html/rfc1951)
- `GZip`, with alias `gz`: [RFC 1952](https://datatracker.ietf.org/doc/html/rfc1952)
- `Zip`, with alias `zipfile`

- `GetSupportedKeys()` returns all known aliases.
- `GetSupportedCompressions()` returns the three supported formats.
- `GetCompression(string alias)` returns the primary name for a given alias.

## Internal Classes

### Class DecompressorFactory

The `DecompressorFactory` offers two instantiation options: `Streaming` and `Buffered`. For better memory performance, prefer `Streaming`, but note that it's forward-only, meaning you cannot detect encoding. If encoding cannot be predefined, use the `Buffered` version instead.

### Single File vs Archive

Formats like GZip and Deflate compress one stream to another — effectively one file into one file. In contrast, an archive contains metadata and multiple file streams.

When using an archive (e.g., with `ZipDecompressor`), the implementation assumes the archive contains a single file. If it doesn't, decompression fails. This constraint exists because the library is focused on delimited (CSV-like) files only.
