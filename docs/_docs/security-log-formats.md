---
title: Security log formats
subtitle: Read CEF and LEEF security events
tags: [usage]
---

Install the optional package (targets .NET 8, 9, and 10):

```bash
dotnet add package PocketCsvReader.SecurityLogs
```

## CEF

`CefReader` accepts CEF versions 0 and 1. The fixed columns, in order, are `CefVersion`, `DeviceVendor`, `DeviceProduct`, `DeviceVersion`, `DeviceEventClassId`, `Name`, and `Severity`. `CefVersion` contains the version number without `CEF:`.

```csharp
using PocketCsvReader.SecurityLogs.Configuration;

var cef = new CefReaderBuilder()
    .WithAttributes("src", "suser", "msg")
    .WithSchema(schema => schema.Named().WithField<int>("Severity"))
    .Build();

using var reader = cef.ToDataReader("security.cef");
while (reader.Read())
{
    var severity = reader.GetInt32(reader.GetOrdinal("Severity"));
    var message = reader.GetString(reader.GetOrdinal("msg"));
}
```

The seven header components must each end with a pipe, including the last component when there are no extensions. Header fields decode `\|` and `\\`. Extension values decode `\=`, `\\`, `\n`, and `\r`; literal pipes and spaces are preserved. Quotes are ordinary value characters, rather than CSV or logfmt quoting. A space followed by a new `key=` starts the next extension, so an equals sign belonging to a value must be escaped. `GetRawString` returns the encoded field text; normal getters use decoded values. Semantic field lengths, severity ranges, and predefined attribute dictionaries are not validated or normalized.

## LEEF

`LeefReader` accepts LEEF 1.0 and 2.0. Its fixed columns are `LeefVersion`, `Vendor`, `Product`, `ProductVersion`, and `EventId`. The delimiter declaration is syntax, not an output column.

```csharp
var leef = new LeefReaderBuilder()
    .WithAttributes("src", "sev", "usrName")
    .WithSchema(schema => schema.Named().WithField<int>("sev"))
    .Build();

var rows = leef.ToArrayString("security.leef");
var table = leef.ToDataTable("security.leef");
var sources = leef.To<string>("security.leef",
    row => row.GetString(row.GetOrdinal("src")));
```

LEEF 1.0 attributes are separated by actual tab characters. LEEF 2.0 requires a delimiter declaration after the event ID, followed by a pipe, for example:

```text
LEEF:2.0|Lancope|StealthWatch|1.0|41|^|src=192.0.2.1^sev=5
LEEF:2.0|Lancope|StealthWatch|1.0|41|x5E|src=192.0.2.1^sev=5
```

A declaration is one literal character or `x`/`0x` followed by one to four hexadecimal digits (`x09` selects tab). NUL, CR, LF, equals signs, and surrogate code units cannot be delimiters. Use hexadecimal notation for a pipe delimiter. Each attribute splits at its first equals sign; spaces, subsequent equals signs, quotes, and backslashes in values remain literal. No CEF unescaping is applied to LEEF. Vendor-specific escape conventions and LEEF 2.0 records omitting the delimiter declaration are unsupported.

## Columns and conversion

Both readers always expose their fixed headers first. `WithAttributes(...)` appends the exact, case-sensitive attribute keys in the requested order; `WithAttributes()` selects only headers. Without this option, keys in the first record establish the remaining columns in input order. The schema stays fixed for the reader's lifetime: later unknown keys are validated then ignored, and absent selected keys return `DBNull.Value` (`null` in string arrays and dictionaries). An empty value is an empty string. Duplicate keys and keys colliding with fixed header names are rejected, including keys not selected for output.

Use `WithSchema` for conversion types, formats, and per-field special-value sequences; it does not select columns. Named and indexed schemas follow the shared APIs, as do `WithResource`, `WithParsers`, `WithParserOptimizations`, and `WithPositionTracking`. Schema types need only be declared for fields requiring conversion. Header and untyped attribute values remain text. Attribute keys cannot contain whitespace, equals signs, pipes, or backslashes.

Both readers support `ToDataReader`, `ToDbDataReader`, `ToDataTable`, `ToArrayString`, `To<T>`, `ToDynamic`, `ToDictionaries`, and `ToObjectArrays`. `To<T>` accepts a mapper or one public constructor with the same number of parameters as output columns, in column order. Materialized strings, arrays, and objects remain valid after advancing the reader.

## Syslog envelopes and record boundaries

Events may be bare or prefixed with:

- An RFC 3164-style `MMM dd HH:mm:ss hostname ` envelope, with a space-padded day accepted, an optional `<PRI>` (0–191), and an optional `app[pid]: ` tag. The timestamp is interpreted only to recognize the envelope; its missing year/timezone is not inferred.
- An RFC 5424 version 1 envelope: `<PRI>1 TIMESTAMP HOSTNAME APP-NAME PROCID MSGID STRUCTURED-DATA MSG`. NILVALUE timestamps and structured data are accepted. Adjacent structured-data elements and quoted/escaped parameter values are skipped before locating the payload. A UTF-8 BOM before the message is accepted.

Envelope fields are discarded, not exposed as event attributes. Other syslog variants, octet-counted network framing, and arbitrary text before an event are unsupported. This is a file reader, not a syslog network receiver or a complete syslog conformance validator.

One physical line is one event, using LF, CRLF, or CR; the final event may lack a newline. Empty input yields no rows; blank lines, malformed headers, malformed attributes, duplicate keys, and invalid CEF escapes throw `InvalidDataException` with format, record number, physical line, and absolute UTF-16 start offset. Multiline events must be encoded into a single line before reading. Stop consuming a reader after a parsing error or cancellation during input I/O; open a fresh reader to restart.

## Streaming and ownership

Synchronous and asynchronous reads share framing and grammar. Input is read in chunks into pooled buffers. Records fitting a chunk borrow its memory; records crossing chunks are coalesced once into pooled record memory. Field metadata and scratch storage are reused. Only CEF values requiring escape decoding allocate decoded strings during ordinary parsing. First-record column discovery allocates column names once. Internal raw record views expire on the next read or disposal; consumers retaining values must materialize them first.

See the [CEF implementation standard](https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-24.4/pdfdoc/cef-implementation-standard/cef-implementation-standard.pdf) and [IBM LEEF event components](https://www.ibm.com/docs/en/dsm?topic=overview-leef-event-components) for format definitions.
