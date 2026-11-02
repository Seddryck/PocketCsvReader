---
title: Project JSON fields and trust stable shapes
tags: [performance]
---

Applies to JSON and NDJSON. Start with [JSON](../json-and-ndjson/) or [NDJSON](../ndjson/) for installation and ordinary reading.

## Why projection improves performance

Consider an event containing a few fields used by an application and a larger diagnostic payload:

```json
{"event":"payment","debug":{"trace":[1,2,3],"message":"details"},"timestamp":"2026-11-02T10:00:00Z","host":"worker-1","tags":["billing","retry"]}
```

If the application only needs `event` and `timestamp`, configure those two properties:

```csharp
var events = new NdjsonReaderBuilder()
    .WithProjection(p => p.Property("event").Property("timestamp"))
    .Build();
```

The same projection APIs are available on `JsonReaderBuilder`. They change the parser's work before the application reads a field:

| Work | Without projection | `WithProjection` | `WithOrderedProjection` |
| --- | --- | --- | --- |
| Read input and validate JSON syntax | Entire record | Entire record | Entire record |
| Build field metadata and nested field trees | All exposed properties | Selected properties only | Selected properties only |
| Match a property to an output column | Discover the record's labels | Look up its name among selected names | Compare its name with the next expected selected name |
| Search for selected names after all have been found | Not applicable | Stops | Stops |
| Allow selected properties in any input order | Yes | Yes | No |

`WithProjection` saves the work of constructing and maintaining field metadata for `debug`, `host`, and `tags`. The parser still checks their JSON syntax, but does not build the nested field trees that would expose their values. The selected fields use a reusable output buffer, and their configured names establish output order. This reduces metadata work and pressure on temporary storage. Skipping unselected values also avoids materialization that would otherwise be needed to expose them; ordinary string values can already be decoded lazily, so projection does not imply that every string was eagerly allocated without it.

Selecting fields in the reader is therefore different from calling `ToDataReader()` without a projection and only reading two columns. Typed access to unused fields may already be lazy, but the unprojected parser still discovers and represents their fields. Projection removes that earlier work. It is also different from applying LINQ `Select` after materializing complete objects, which has already paid the cost of creating those objects.

`WithOrderedProjection` keeps those savings and reduces the cost of finding selected fields. With ordinary projection, the scanner computes a name fingerprint and looks up which selected column it matches. Ordered projection compares against only the next expected name, then advances to the following selected column. It avoids the general name-lookup table and fingerprint computation. Unselected properties can still occur between matches: the `debug` property above is allowed between `event` and `timestamp`.

```csharp
var events = new NdjsonReaderBuilder()
    .WithOrderedProjection(p => p.Property("event").Property("timestamp"))
    .Build();
```

Both examples expose columns in the order `event`, `timestamp`. If the input instead puts `timestamp` before `event`, ordinary projection succeeds; ordered projection rejects the record because its order contract is broken. Use ordered projection only when the producer guarantees that selected properties retain their relative order. It does not require every property or nested structure to remain identical; that is a separate stable-object-shape contract.

Both projection modes read the rest of the record and reject invalid unselected JSON. After finding the last selected field, the scanner stops name lookup but continues syntax validation to the record boundary. Malformed data in a later `tags` value still fails. On the combined framing/projection paths described below, one scan also locates that boundary, avoiding a separate framing pass.

The benefit is usually most noticeable when selecting a small subset of a wide object, especially when omitted properties contain nested objects or arrays. If nearly every field is selected, little metadata work is avoided. Ordered projection's additional benefit depends on the property names, their positions, and the input workload. Neither option reduces input bytes or guarantees a fixed speedup; measure representative input and include the fields your application actually reads.

## JSON projection

`JsonReaderBuilder.WithProjection` validates the complete JSON value while materializing only selected properties. The JSON reader combines projection with object framing, reuses field metadata, compares property names without creating a string for ordinary unescaped names, and stops projection lookup once all selected fields have been found.

`JsonReaderBuilder.WithOrderedProjection` is a faster opt-in contract. Selected properties must occur in the configured order, although unselected properties may occur between them. Reordered selected properties are rejected.

## NDJSON projection

`NdjsonReaderBuilder.WithProjection` exposes selected properties in configuration order and accepts those properties in any input order. For eligible configurations, the reader locates the record boundary and selected fields in one pass through the input. It validates the complete JSON value, skips unselected values, and populates a reusable projected-field buffer. Property-name fingerprints support lookup; lookup stops once all selected fields have been found, while validation continues.

This combined path is selected automatically for ordinary or ordered projection when all of the following hold:

- The line terminator is LF (`"\n"`) or CRLF (`"\r\n"`).
- Comments are disabled.
- The configured whitespace characters are exactly space, tab, carriage return, and line feed.
- Neither `WithRecordPath` nor `WithStableObjectShape` is enabled.

`NdjsonReaderBuilder.WithOrderedProjection` avoids the fingerprint lookup by comparing each property with the next expected selected name. Unselected properties may occur between selected properties; reordered selected properties are rejected.

Other configurations retain the compatible path: frame the complete record first, then parse or project its JSON values. Custom terminators, comments, record-path selection, and stable-shape parsing therefore must not be described as using combined framing and projection. Both paths preserve format validation, synchronous/asynchronous reads, and records spanning buffer boundaries.

## NDJSON stable object shape

`NdjsonReaderBuilder.WithStableObjectShape` is an opt-in trusted-input contract for streams whose objects always have the same recursive JSON structure and property order. The first object establishes object property counts, array lengths, labels, and ordinals at every level. Later objects validate property-name syntax but skip decoding, comparison, and lookup, then write values directly by ordinal into reusable field-array trees. Property-count, array-length, and scalar/container drift throws before a record is exposed. Same-count renames and reorders are deliberately not detected, so callers must use this option only for trusted stable-shape input. Scalar leaf types remain dynamic unless a schema or typed accessor establishes and caches a conversion for an ordinal.
