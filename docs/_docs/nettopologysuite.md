---
title: NetTopologySuite geometry types
tags: [configuration, parsing]
---

Install the optional `PocketCsvReader.NetTopologySuite` NuGet package to read geometry columns as NetTopologySuite objects. The adapter supports .NET 8, 9, and 10.

```shell
dotnet add package PocketCsvReader.NetTopologySuite
```

## Read WKT and GeoJSON in the same CSV

```csharp
using NetTopologySuite.Geometries;
using PocketCsvReader.Configuration;
using PocketCsvReader.NetTopologySuite;

var csv = new CsvReaderBuilder()
    .WithDialect(d => d.WithLineTerminator("\n"))
    .WithParsers(p => p.UseNetTopologySuite(wktSrid: 4326))
    .WithSchema(s => s.Named()
        .WithField<Geometry>("route", f => f.WithFormat("wkt"))
        .WithField<Point>("stop", f => f.WithFormat("geojson")))
    .Build();

using var rows = csv.ToDataReader("routes.csv");
int routeColumn = rows.GetOrdinal("route");
int stopColumn = rows.GetOrdinal("stop");
while (rows.Read())
{
    Geometry route = rows.GetFieldValue<Geometry>(routeColumn);
    Point stop = rows.GetFieldValue<Point>(stopColumn);
}
```

`routes.csv` contains a WKT line and a GeoJSON point. The inner double quotes in the GeoJSON cell are doubled according to CSV quoting rules.

```csv
route,stop
"LINESTRING (4.35 50.85, 4.36 50.86)","{""type"":""Point"",""coordinates"":[4.35,50.85]}"
```

## Read a nested GeoJSON point from JSON

A JSON record can contain a GeoJSON geometry as an object property. For example, save this record as `places.json`.

```json
{"location":{"type":"Point","coordinates":[4.35,50.85]}}
```

Use `JsonReaderBuilder` and declare `location` as a `Point` with the `geojson` format.

```csharp
using NetTopologySuite.Geometries;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.NetTopologySuite;

var json = new JsonReaderBuilder()
    .WithParsers(p => p.UseNetTopologySuite())
    .WithSchema(s => s.Named()
        .WithField<Point>("location", f => f.WithFormat("geojson")))
    .Build();

using var rows = json.ToDataReader("places.json");
if (rows.Read())
{
    Point location = rows.GetFieldValue<Point>("location");
}
```

For a GeoJSON `FeatureCollection`, add `WithRecordPath("$.features[*]")` and declare `geometry` as the field name. Each selected feature becomes a row. See [JSON record paths](../json-record-paths/).

## Formats and supported types

| Field format | Input | Spatial reference ID |
| --- | --- | --- |
| Omitted or `wkt` | Well-known text | `wktSrid`, which defaults to `0`. An explicit SRID in the WKT input takes precedence. |
| `geojson` | A single GeoJSON geometry object | `4326`. Positions use longitude, latitude, and optional altitude in that order. |

Format names are case-insensitive. WKT syntax follows NetTopologySuite's WKT reader. Assigning an SRID leaves the coordinates in their original units. An unknown format throws `ArgumentException` when the field parser is resolved.

`UseNetTopologySuite()` registers defaults for `Geometry`, `Point`, `LineString`, `Polygon`, `MultiPoint`, `MultiLineString`, `MultiPolygon`, and `GeometryCollection`. Use `Geometry` for a column with varying shapes. Use a concrete type to require a compatible shape. Polygon holes and nested geometry collections are supported. GeoJSON `Feature` and `FeatureCollection` objects have their own document structure. See [JSON record paths](../json-record-paths/) for selecting features as rows.

An explicit field parser takes precedence over the integration. An explicit runtime `WithParser` registration for a supported type overrides its integration default in either registration order, including on columns with a format. Invalid geometry input and shape mismatches throw `FormatException`. The data reader applies its normal CSV null and sequence behavior before parsing.

## How the parsers receive field text

Both formats consume the existing **logical CSV field** without copying the entire cell into another string or character array at the parser boundary. Typed `GetFieldValue<T>` and boxed `GetValue` both pass the existing backing memory for that access.

- **WKT** wraps the field's `ReadOnlyMemory<char>` in a small `TextReader`. NetTopologySuite tokenizes the text directly from that reader. PocketCsvReader consumes the borrowed memory synchronously before the current row can be released or overwritten.
- **GeoJSON** uses PocketCsvReader's own JSON parser. The adapter uses `ReadOnlySpan<char>` slices for properties and numeric coordinates and builds the geometry with NetTopologySuite's `GeometryFactory`. The adapter has no dependency on `System.Text.Json` or the NetTopologySuite GeoJSON converter.

Byte-backed CSV input still needs UTF-8 decoding. CSV fields with doubled quotes can require an unescape buffer, and escaped JSON member names or type names can require decoding. NetTopologySuite still allocates for WKT tokenization and for the resulting geometry. Parsed geometries own their data and remain usable after the reader advances.

The integration tests compare allocation for short geometry cells with cells padded by 32 KiB of whitespace. They exercise typed and boxed getters through the CSV reader on both character and native UTF-8 input. The WKT check subtracts the allocation of directly invoking the same NetTopologySuite tokenizer using a reader over an existing string. The GeoJSON check also covers ordinary double-quoted CSV cells and allows one buffer for CSV unescaping. The remaining allocation stays below what an extra full-cell string or array would require.
