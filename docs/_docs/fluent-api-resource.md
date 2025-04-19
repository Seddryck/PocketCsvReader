---
title: Fluent API for Resource Configuration
tags: [configuration]
---

## Overview

The Fluent API for resource configuration in PocketCsvReader provides an intuitive and flexible way to define resource-level settings, such as file encoding and value substitution sequences.

## Accessing the Resource Descriptor

You can configure resource settings using the `CsvReaderBuilder`, class and the `WithResource` method, which allows you to instantiate a `ResourceDescriptorBuilder`:

```csharp
var builder = new CsvReaderBuilder().WithResource
(
    new ResourceDescriptorBuilder()
                .WithEncoding("utf-8")
);
var reader = builder.Build();
```

This method allows you to define **resource-level behaviors**, including encoding settings and sequences.

## Configuring File Encoding

PocketCsvReader can automatically detect file encoding based on the Byte Order Mark (BOM). The BOM is a sequence of bytes at the beginning of a file that indicates the encoding format.

However, some CSV files do not include a BOM, and certain encodings lack a defined BOM. In such cases, you can manually specify the encoding using the `WithEncoding` method.

```csharp
var builder = new CsvReaderBuilder().WithResource
(
    new ResourceDescriptorBuilder()
                .WithEncoding("ISO-8859-2")
);
var reader = builder.Build();
```

### Supported Encodings

The `WithEncoding` method expects a MIME-type encoding name. Case sensitivity is not enforced when validating MIME types.

Examples:

- `WithEncoding("ISO-8859-2")`
- `WithEncoding("utf-8")`

## Configuring Compression

PocketCsvReader can automatically decompress delimited-files compressed with a classical compression format.

```csharp
var builder = new CsvReaderBuilder().WithResource
(
    new ResourceDescriptorBuilder()
                .WithCompression(CompressionFormat.Gzip)
);
var reader = builder.Build();
```

More info on [compression](../compressed-files).

### Registering Sequences

A sequence substitution allows you to replace specific values in the CSV data before parsing. The concept of sequences is explained in detail [here](/docs/sequences).

You can define sequences using the WithSequence method of the ResourceDescriptorBuilder class. This method allows you to specify:

1. The pattern to match in the CSV data.
2. The replacement value to substitute in place of the matched pattern.

```csharp
var builder = new CsvReaderBuilder()
    .WithResource(
        (r) => r.WithSequence("0", "-1")
    );
```

 In this example, all occurrences of "0" in the CSV data will be replaced with "-1" before parsing.
