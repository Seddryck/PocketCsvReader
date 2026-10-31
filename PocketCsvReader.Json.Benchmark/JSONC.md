# JSONC benchmark notes

Measured on 2026-10-31 using Windows 11 x64, Intel Core Ultra 9 275HX, .NET 10.0.14, BenchmarkDotNet 0.15.8, and SDK 11.0.100-rc.2.26504.105. Short runs use one launch, one warmup, and three measured iterations. These are local comparative measurements, not a throughput guarantee; confidence intervals are wide with only three iterations.

## Initial JSONC implementation: strict JSON before and after

The initial measurements in this section and the buffer-size section describe commit `f8e1806`, before the independent options were introduced.

The baseline is `origin/main` at `3b0c9dd`, before any parser changes. `JsonParserBenchmarks` parses 50,000 flat objects with 8–12 properties, projecting `name`, `amount`, and `count`. Setup verifies identical aggregate results across all variants. JSONC fixtures add block comments, line comments between objects, and object/root-array trailing commas. Input generation is outside the measurement.

| Input and dialect | Ordered projection | Normal projection | Allocated per document |
| --- | ---: | ---: | ---: |
| Strict JSON before | 22.90 ms | 28.31 ms | 2.32 MB |
| Strict JSON after, default dialect | 22.61 ms | 29.02 ms | 2.32 MB |
| Strict JSON after, JSONC enabled | 30.23 ms | 40.19 ms | 2.32 MB |
| JSONC after, JSONC enabled | 33.79 ms | 44.29 ms | 2.32 MB |

The default dialect changed by approximately -1.3% ordered / +2.5% normal in these short samples. Opting in to JSONC on the same strict input added approximately 34% / 38% compared with the default dialect after the change. The fixture with comments and trailing commas took approximately 49% / 53% longer than strict JSON after the change, including the extra input text. Allocations remained 2.32 MB in all six after variants. Later variants showed greater timing variability, so these percentages should not be treated as precise overhead bounds.

The baseline benchmark temporarily selected BenchmarkDotNet's default high-performance plan, which was already active and was restored on completion. Subsequent runs keep the current power plan explicitly.

Reproduce after measurements from the repository root:

```powershell
dotnet run --project PocketCsvReader.Json.Benchmark -c Release -f net10.0 -- --filter '*PocketCsvReaderJson*' '*JsoncEnabled*' --job short --warmupCount 1 --iterationCount 3 --artifacts BenchmarkDotNet.Artifacts/jsonc/after
```

For the baseline, run the two original `*PocketCsvReaderJson*` benchmarks at the baseline commit with the same job parameters.

## Record sizes and buffer crossings

`JsoncBufferBenchmarks` reads 1,000 two-property objects without projection, with a 64- or 4,096-character string and a 128- or 65,536-character parser buffer. It compares equivalent strict JSON and JSONC. The 4,096-character records exceed the small buffer, while the large buffer exercises the single-buffer path as well as occasional record crossings. This benchmark includes existing general-parser field metadata allocations; it does not measure skipped-property projection.

```powershell
dotnet run --project PocketCsvReader.Json.Benchmark -c Release -f net10.0 -- --filter '*JsoncBufferBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --artifacts BenchmarkDotNet.Artifacts/jsonc/buffers
```

| String characters | Buffer characters | Strict JSON | JSONC | Strict allocated | JSONC allocated |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 64 | 128 | 398.7 us | 487.6 us | 488.13 KB | 497.38 KB |
| 64 | 65,536 | 312.2 us | 402.0 us | 414.06 KB | 414.06 KB |
| 4,096 | 128 | 8,186.6 us | 9,157.0 us | 1,612.56 KB | 1,691.37 KB |
| 4,096 | 65,536 | 5,305.9 us | 5,517.8 us | 421.21 KB | 421.33 KB |

Extra comment text changes buffer-crossing frequency, so small buffers show additional existing capture bookkeeping allocations. The large-record/large-buffer timing samples had particularly wide confidence intervals. These figures include general-parser field metadata, not full-document normalization or retained document storage.

The JSONC regression suite separately checks raw projected parsing against a warmed budget of 64 bytes per record with 2 KiB of skipped text, reused field metadata and character storage, and first-row delivery before the complete non-seekable document is read. Comments retain original source offsets and raw composite text. Record-path selection retains its existing complete-document capture; ordinary top-level arrays stream one element at a time.

## Independent options revision

Reran the six projection variants after replacing the combined flag with independent `AllowComments` and `AllowTrailingCommas` settings, using the same workload and short-job parameters. The builder shorthand sets both to `true`.

| Input | AllowComments | AllowTrailingCommas | Ordered projection | Normal projection | Allocated per document |
| --- | --- | --- | ---: | ---: | ---: |
| Strict JSON | false | false | 21.95 ms | 29.91 ms | 2.32 MB |
| Strict JSON | true | true | 23.54 ms | 34.19 ms | 2.32 MB |
| JSONC | true | true | 27.57 ms | 34.78 ms | 2.32 MB |

Allocations remained unchanged in this comparison. The three-iteration timing intervals were wide, especially for normal projection and opt-in on strict input, so differences from earlier samples cannot be attributed precisely to the option split. Raw allocation and ownership regression tests also passed on all three target frameworks. The eight buffer-size cases above were not rerun for this revision.

Reproduce with the projection command above and `--artifacts BenchmarkDotNet.Artifacts/jsonc/independent-options`.
