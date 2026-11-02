---
title: Encoding and stream initialization
tags: [input]
---

<span id="accessing-the-resource-descriptor"></span>
<span id="configuring-compression"></span>
<span id="configuring-file-encoding"></span>
<span id="overview"></span>
<span id="supported-encodings"></span>

Configure input encoding before creating the reader. The resource descriptor also carries compression and sequences, whose authoritative guides are [Compression](../compressed-files/) and [Null and missing values](../sequences/).

## Configure an encoding

```csharp
var csv = new CsvReaderBuilder()
    .WithResource(r => r.WithEncoding("utf-8"))
    .Build();
```

`WithEncoding` accepts an encoding name recognized by .NET, such as `utf-8`. Some legacy code pages require the application's encoding provider registration. It describes decompressed text when compression is configured. It is not a compression or filename-extension detector.

## Probing and BOMs

CSV's `StreamInitializationOptions` controls encoding probing and parser selection. Explicit resource encoding has precedence over probing. Without an explicit encoding, probing recognizes supported BOMs; input without one falls back to UTF-8. Known UTF-8 input can use `StreamInitializationOptions.Utf8`, which disables probing and requests native byte parsing subject to its eligibility rules. `ForwardOnly` disables probing while retaining the default character input mode.

```csharp
var csv = new CsvReaderBuilder()
    .WithStreamInitialization(new StreamInitializationOptions(
        ProbeEncoding: true, InputMode: CsvInputMode.Auto))
    .Build();
```

The matching initial encoding preamble is consumed once; later U+FEFF characters remain data. With probing disabled, another encoding's BOM does not select that encoding. A non-seekable stream cannot be rewound by the caller; probe handling and ordinary forward-only parsing must not be confused with [inference replay](../schema-inference/#streams).

See [native UTF-8 input](../utf8-input/) for the selection matrix, unsupported dialects, replacement fallback, and decompression behavior. Other format profiles can enforce their own initialization policy; do not assume CSV's native byte parser exists for every format.

## Compression

Compression is explicitly configured by `WithCompression` and disabled by `WithoutCompression`. See [Read compressed files](../compressed-files/) for examples, aliases, factory modes, and ZIP constraints. Ordinary reader configuration does not infer compression from filename extensions; inference has its own documented filename handling.

## Registering sequences

`WithResource(r => r.WithSequence("N/A", null))` registers a resource-level replacement for readers that apply the sanitizer. Replacements match whole eligible field values, not arbitrary substrings in the file. Quoted/escaped fields and native JSON nulls have distinct rules. See [value handling](../sequences/).

For disposal, lazy enumeration, cancellation, and already-open streams see [Stream ownership](../stream-lifetimes/).
