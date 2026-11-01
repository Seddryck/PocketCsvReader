# Optimized unquoted CSV benchmark cases

The comparison now contains fourteen cases. `CsvCharsOptimized` and
`CsvUtf8Optimized` complement the existing `CsvChars` and `CsvUtf8` cases so
dialect/sanitization costs can be compared within each input mode.

All four use the same 4,409,248 UTF-8 input bytes, 50,000 seven-field records,
and `(string Request, int Status, long Bytes)` output. The generated fields contain
no commas, line breaks, quotes or escape syntax; every record has seven fields.

The optimized cases explicitly configure:

```csharp
.WithDialect(d => d.WithoutHeader().WithLineTerminator("\n")
    .WithoutQuoteChar().WithoutEscapeChar().WithoutDoubleQuote()
    .WithCommentChar((char?)null).WithoutCommentRows()
    .WithoutHeaderRepeat().WithoutSkipInitialSpace().WithoutNullSequence())
.WithParserOptimizations(options with
{
    UnescapeChars = false,
    HandleSpecialValues = false,
    ExtendIncompleteRecords = false
})
```

`QuoteChar = null` selects the existing `UnquotedFieldParser` through
`FieldParserFactory`, removing the general parser's quoted-field states.
Disabling unescaping and special-value handling simplifies value access;
incomplete-row extension is unnecessary for this fixed-schema input. Several
settings (such as a null escape/comment character and disabled whitespace
skipping) already match baseline defaults and are explicit workload assumptions,
rather than additional speedups.

Buffer size remains 4 KiB. String pooling, generated data, projections, position
tracking, read-ahead, startup row counting and stream initialization remain
matched to the corresponding baseline. Character cases use forward-only decoded
input; UTF-8 cases use the explicit UTF-8 byte mode. No parser implementation or
ownership contract changes are involved. These settings are appropriate for this
known unquoted workload; they do not provide quoted CSV semantics.

## Reproduction and validation

```powershell
dotnet build benchmarks/WebLogParserComparison/WebLogParserComparison.csproj -c Release
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --validate
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --matrix
dotnet benchmarks/WebLogParserComparison/bin/Release/net10.0/WebLogParserComparison.dll --filter '*CsvChars*' '*CsvUtf8*'
```

All fourteen setups validate all 50,000 three-value tuples before timing. Setup
validation passed on .NET 8, 9 and 10 (2.1 million row checks). The benchmark
project built on all three frameworks with no warnings/errors, its analyzer
verification passed, and the full solution Release build passed with existing
test/profiler warnings. Unit test suites were not rerun for these benchmark-only
configuration changes; parser grammar and ownership code were not modified.

The matrix uses two seconds of warmup and seven batches of at least one second
per case, measuring process CPU and current-thread allocated bytes. The CSV
BenchmarkDotNet comparison uses isolated processes with three warmup and three
measurement iterations. Input generation, GC setup and tuple validation are
outside timing. CPU and elapsed results come from different measurement runs;
do not combine them to derive utilization. Point estimates from sequential
short runs do not establish a universal speedup.

The controlled-worker entry point also accepts both new names. Smoke runs used
one second of warmup and two one-second batches per optimized mode; both reported
the identical input SHA-256
`189F138FE8FDFBC62D74660474D1F1EAE3AEAF184DD61E404A178D925644A5F8`.
These smoke runs check the entry point and checksum, rather than establishing a
controlled performance comparison.

## Results per 50,000 records

| CSV mode | Process CPU, matrix | Elapsed, BenchmarkDotNet | Allocated bytes, matrix |
|---|---:|---:|---:|
| Characters | 31.455 ms | 45.40 ms | 4,370,453 |
| Characters optimized | 27.708 ms | 20.12 ms | 4,369,652 |
| UTF-8 | 23.087 ms | 28.66 ms | 4,365,899 |
| UTF-8 optimized | 23.919 ms | 13.74 ms | 4,365,236 |

Character input improved in both runs, but the size of that improvement differed
substantially. UTF-8 improved in the isolated elapsed-time run but did not improve
in the process-CPU matrix. These sequential short runs do not support a single
reliable percentage improvement; comparisons across their CPU/elapsed columns
are not valid. A paired, alternating configuration comparison would be needed
to establish a precise effect. Allocations are essentially unchanged: the small
differences are reader setup costs, while the required request strings dominate.

All fourteen BenchmarkDotNet cases completed. Its short-run report includes a
minimum-iteration-time warning (88.726 ms observed versus 100 ms recommended),
and several broad timing intervals. Raw results are retained rather than
discarding those limitations.

The full matrix is saved in `csv-optimized-matrix.csv`. The independent elapsed
and allocation reports are `csv-optimized.bdn.csv` and `csv-optimized.bdn.md`;
BenchmarkDotNet's memory display uses 1,024-based units, while matrix allocations
are exact bytes. The broader benchmark ran all fourteen cases because `*Csv*`
also matches the namespace; the reproduction command above narrows the filter
to the four CSV parameter names.
