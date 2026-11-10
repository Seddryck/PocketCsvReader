---
title: Overview and supported formats
tags: [quick-start]
---

PocketCsvReader reads structured text through a forward-only data reader. Start with the core CSV package and add the extension for your input format. Schema and custom parser settings control value conversion; output APIs let you consume fields, objects, arrays, dictionaries, or tables.

## Start here

1. [Install the package for your format](../installation/).
2. [Read your first CSV](../basic-usage/), or choose a format below.
3. [Choose an output model](../output-models/) and review [stream ownership](../stream-lifetimes/).

## Supported inputs

| Input | Guide | Package |
| --- | --- | --- |
| CSV, TSV, other delimiters | [CSV and TSV](../csv-dialect-descriptor/) | `PocketCsvReader` |
| JSON objects/arrays and opt-in JSONC | [JSON and JSONC](../json-and-ndjson/) | `PocketCsvReader.Json` |
| Independently framed JSON values | [NDJSON](../ndjson/) | `PocketCsvReader.Ndjson` |
| Character-position records | [Fixed-width](../fixed-width/) | `PocketCsvReader.FixedWidth` |
| Labeled tab-separated and key=value records | [LTSV and logfmt](../key-value-formats/) | `PocketCsvReader.KeyValue` |
| Common, Combined, and W3C logs | [Web logs](../web-log-formats/) | `PocketCsvReader.WebLogs` |
| CEF and LEEF events | [Security logs](../security-log-formats/) | `PocketCsvReader.SecurityLogs` |

JSON and NDJSON support property projection and stable field layout for object records. Logfmt and LTSV support column projection with aliases and stable field layout. See [JSON and NDJSON projection](../json-projection/) and [logfmt and LTSV projection](../key-value-formats/#project-a-fixed-set-of-columns).

## Inputs that produce Arrow batches directly

| Input | Guide | Package |
| --- | --- | --- |
| ClickHouse JSONColumns, JSONColumnsWithMetadata, and Arrow Integration JSON | [Columnar JSON](../columnar-json/) | `PocketCsvReader.Arrow.ColumnarJson` |
| Textual NetCDF datasets (.cdl) | [NetCDF CDL](../netcdf-cdl/) | `PocketCsvReader.Arrow.NetCdf` |

## Prepare and interpret input

If the CSV structure is unknown, [infer a schema and dialect](../schema-inference/), inspect diagnostics, then accept or adjust the result. Inference reads a sample; it is a discovery step before explicit configuration.

[Profiles and builders](../fluent-api-profile-configuration/) combine syntax, schema, resource, parser, and optimization settings. [Null and missing values](../sequences/) and [error handling](../validation-and-errors/) differ by format and output API.

## Boundaries and limitations

The readers process files and streams; they are not writers or network log receivers. Streaming avoids loading the complete input in ordinary row-reading paths, but memory also depends on the largest record, selected output model, and format. `ToDataTable` materializes all rows. JSON record-path selection captures a complete document before selecting rows; ordinary top-level array reading streams its elements.

See [parser optimizations](../parser-optimizations/) for the input contracts and performance considerations for stable field layout and UTF-8 CSV input.

Optional integrations expose [Apache Arrow batches](../apache-arrow/) and [Chrononuensis temporal types](../chrononuensis/). Package support and framework requirements are listed in [installation](../installation/).
