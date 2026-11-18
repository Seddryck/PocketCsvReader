# Generic custom-parser boxing comparison

Generic string-parser registrations now keep a typed `ParseSpan<T>` adapter for
reader access. The object-returning wrapper remains available through public
descriptor enumeration, but typed reads no longer invoke it. The non-generic
`Type`/`ParseFunction` registration API is unchanged.

## Versions and method

Measured on November 19, 2026:

- Before: `15897453fedfc265ca4ad01a5355102adc85cff8` (the fetched `origin/main`
  used to create this implementation branch).
- After: the `RuntimeParsersDescriptor.AddParser<T>(ParseFunction<T>)` change in
  this PR. No implementation of #487 is included.
- Identical `CustomParserBoxing.cs` and benchmark registration in `Program.cs`
  were copied to the detached baseline worktree. Each version was built in
  Release and executed sequentially in a separate process on the same machine.
- BenchmarkDotNet 0.15.8, .NET 10.0.15 x64 RyuJIT, Windows 11
  (10.0.26300.9974), Intel Core Ultra 9 275HX. SDK:
  `11.0.100-rc.2.26504.105`.
- In-process emit toolchain, five warm-up iterations, ten measurement iterations,
  250 ms per iteration, memory diagnoser, no power-plan changes.

Every case repeatedly reads the current field containing `3000000`, after setup,
the initial record read, and lazy typed/boxed adapter initialization. There are no
row filters or schema formats overriding the runtime registration. This isolates
field conversion; it does not measure file I/O, row advancement, registration
cost, or end-to-end import throughput.

`UseBuilder = false` uses `RuntimeParsersDescriptor.AddParser<T>` directly;
`true` uses `RuntimeParsersDescriptorBuilder.WithParser<T>`. The custom `Amount`
struct holds one `long`. The string control returns the materialized input string;
the span control uses a span parser directly. Controls distinguish retained input
string allocations from boxing.

Run the following from each worktree after building its benchmark project:

```powershell
dotnet build PocketCsvReader.Benchmark/PocketCsvReader.Benchmark.csproj -c Release -f net10.0
dotnet PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll --filter '*CustomParserBoxing*' --job short --inProcess --warmupCount 5 --iterationCount 10 --iterationTime 250 --artifacts BenchmarkDotNet.Artifacts/boxing
```

To reproduce the baseline, create a detached worktree at the before revision and
copy `CustomParserBoxing.cs` and the updated `Program.cs` into its benchmark project
before building. Leave its runtime sources at the before revision.

## Results

Times are means per field access. Allocations are managed bytes per access.

| Registration | Method | Before | After | Time change | Before allocation | After allocation |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Descriptor | TypedInt32 | 44.65 ns | 34.88 ns | -21.9% | 64 B | 40 B |
| Descriptor | TypedInt64 | 40.64 ns | 37.44 ns | -7.9% | 64 B | 40 B |
| Descriptor | TypedStruct | 43.15 ns | 39.21 ns | -9.1% | 64 B | 40 B |
| Descriptor | BoxedInt32 | 42.74 ns | 38.32 ns | -10.3% | 88 B | 64 B |
| Descriptor | StringInputControl | 50.01 ns | 47.21 ns | -5.6% | 40 B | 40 B |
| Descriptor | SpanControl | 37.29 ns | 36.49 ns | -2.1% | 0 B | 0 B |
| Builder | TypedInt32 | 40.65 ns | 34.55 ns | -15.0% | 64 B | 40 B |
| Builder | TypedInt64 | 40.52 ns | 37.23 ns | -8.1% | 64 B | 40 B |
| Builder | TypedStruct | 43.11 ns | 38.93 ns | -9.7% | 64 B | 40 B |
| Builder | BoxedInt32 | 44.16 ns | 38.77 ns | -12.2% | 88 B | 64 B |
| Builder | StringInputControl | 50.09 ns | 47.54 ns | -5.1% | 40 B | 40 B |
| Builder | SpanControl | 35.86 ns | 37.63 ns | +4.9% | 0 B | 0 B |

Both generic registration paths eliminate one 24-byte intermediate box per value.
Typed value reads allocate 37.5% fewer bytes in these cases. Object reads allocate
27.3% fewer bytes and retain the final box required by `GetValue()`.

The 40-byte input string is still allocated for every string-parser invocation.
This change does not make string-based parsing allocation-free. Existing row-filter
value caches can also box values independently of this registration change.

Timings improved for the changed numeric and struct cases in this run, but even
the controls varied between versions. Treat these as local microbenchmark results,
not a general throughput guarantee. Allocation removal is the primary result.
Benchmarks on .NET 8/9 and full-file throughput were not run.

Full reports include error (half of the 99.9% confidence interval), standard
deviation, and Gen0 collections per 1,000 operations:

- [Before Markdown](CustomParserBoxing.before.md) and [CSV](CustomParserBoxing.before.csv)
- [After Markdown](CustomParserBoxing.after.md) and [CSV](CustomParserBoxing.after.csv)

## Validation

- Release builds of `PocketCsvReader.sln` and `PocketCsvReader.full.sln` passed for
  all declared target frameworks. Existing dependency/test-adapter warnings remain.
- Core focused tests: 75 passed on each of `net8.0`, `net9.0`, and `net10.0`, using
  the filter `FullyQualifiedName~CustomParserBoxingTests|FullyQualifiedName~SpanCustomParserTests|FullyQualifiedName~StringParserGetterTests|FullyQualifiedName~SchemaArrayTest|FullyQualifiedName~ReaderFilteringTest`.
- Indexing focused tests: 32 passed on each of those frameworks, using the filter
  `FullyQualifiedName~RuntimeParser|FullyQualifiedName~SparseIndex`.
- `dotnet format PocketCsvReader.full.sln analyzers --verify-no-changes --no-restore`
  passed. The workspace loader reported warnings.
- Both benchmark runs completed all 12 cases.
- Full test suites and unrelated format-specific test projects were not run.
