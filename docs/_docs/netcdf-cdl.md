---
title: NetCDF CDL as Arrow record batches
tags: [analytics, arrow, netcdf]
---

Install the optional package to read UTF-8 Common Data Language files (.cdl), the textual representation of a NetCDF dataset. Binary NetCDF files (.nc) require a separate NetCDF library. [Unidata CDL specification](https://docs.unidata.ucar.edu/nug/current/_c_d_l.html)

~~~bash
dotnet add package PocketCsvReader.Arrow.NetCdf
~~~

~~~csharp
using PocketCsvReader.Arrow;
using PocketCsvReader.Arrow.NetCdf;

var reader = new NetCdfCdlReader(new NetCdfCdlOptions
{
    RowDimensions = ["time", "latitude", "longitude"],
    DataVariables = ["temperature"],
    BatchSize = 4096,
});

await foreach (var batch in reader.ToArrowBatchesAsync("weather.cdl", cancellationToken))
{
    using (batch)
    {
        // Consume the coordinate and temperature columns.
    }
}
~~~

Use the file or stream overload of ToArrowStream or ToArrowStreamAsync to inspect the schema before reading batches. Both overloads read from the input's current position and support seekable and forward-only inputs. By default a supplied stream remains open. Setting LeaveOpen to false disposes it when opening finishes or fails. File overloads close the input after staging. Dispose the returned Arrow stream or batch enumerator to delete its temporary file, and dispose each returned batch independently.

## Table shape

- RowDimensions lists the axes from slowest to fastest. With the order time, latitude, longitude, longitude varies fastest. A variable's values are interpreted in its declared CDL order and mapped by dimension name, so it may use a different order or a subset of the row dimensions. A scalar broadcasts across the whole grid.
- When RowDimensions is omitted, the widest selected shape must contain every selected variable's dimensions, and equally wide shapes must agree in order. With no data variables, inference uses coordinate declarations if present or a single declared dimension. An ambiguous shape requires RowDimensions and, for incompatible variables, a narrower DataVariables selection.
- The first columns correspond to the row dimensions. A variable is a coordinate when it has the same name as its sole dimension. If a coordinate is undeclared, the reader emits a zero-based Int64 index. A declared coordinate with missing values follows the usual null rules.
- DataVariables chooses the remaining columns in order. Omit it to select all non-coordinate variables in declaration order, or pass an empty list for coordinates only. An explicitly listed coordinate appears only once. Dimension identity is based on declarations, and the reader rejects variables using dimensions outside the chosen grid.
- A scalar-only dataset with data has one row. A dataset with no assignments or a selected zero-length dimension exposes its schema and yields zero batches.

## Supported CDL

- The reader accepts the dimensions, variables, and data sections, fixed dimensions with nonnegative Int64 extents, and one unlimited dimension when it is the first axis of each variable that uses it. The unlimited extent is inferred from the largest number of records supplied by any assignment, rounding partial records up.
- Signed and unsigned integer types of 8, 16, 32, and 64 bits map to the corresponding Arrow types. Float and double map to Arrow Float and Double. CDL numeric suffixes, octal and hexadecimal integer constants, and floating-point NaN and Infinity are supported. Integer destinations require integral literals that fit the target type.
- Global and variable attributes become Arrow schema and field metadata. A single quoted value is decoded, and lists are retained as comma-separated CDL literals. The reader adds netcdf:name, netcdf:dimensions, and netcdf:shape metadata to the schema and records each column's source dimensions and shape on its field. Text attributes support common C escapes such as escaped quotes, backslashes, tabs, and newlines.
- An underscore, an explicit numeric _FillValue or missing_value match, and cells omitted from an assignment all become Arrow nulls. A short assignment is padded with nulls. Surplus values are rejected. Numeric literals equal to a NetCDF default fill constant remain numeric unless an attribute declares them missing.
- Line and block comments are supported. Groups, user-defined types, char and string variables, repeated dimensions within a variable, escaped names, and repeat-count expressions are outside this subset. A malformed document is rejected during opening, before any batches are returned.

## Staging and encodings

Opening parses the document once and stages nine bytes per supplied value in a temporary file, even when the source is seekable. The reader keeps a character buffer, reusable token scratch, declaration metadata, and a fixed-size write buffer in memory. Batch reads use one fixed-size page per selected column plus the current Arrow batch. Opening consumes the whole input before returning the schema. Sequential variable access uses sequential pages, while dimension reordering and some broadcasts can cause extra page reads.

Dense arrays are the default for Arrow IPC interoperability. RunEndEncodeCoordinates uses Arrow run-end encoded arrays for coordinate columns and indices, with batch-relative Int32 run ends. The column names, underlying value types, and logical row counts are the same in both modes. The encoded schema advertises the physical Arrow run-end type. Both encodings round-trip through the Arrow .NET 23.0.0 IPC stream writer and reader in the adapter's tests. Support for the encoded schema depends on the downstream reader. [Apache Arrow .NET 23.0.0 release](https://github.com/apache/arrow-dotnet/discussions/352)
