# CSV zone-map benchmark

From the repository root, run the benchmark with the .NET 10 SDK and runtime.

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --zone-map
```

The [benchmark](ZoneMapBenchmark.cs) generates 262,144 synthetic orders with seed 431, including quoted and multiline fields. It uses the same orders in three layouts. The clustered layout shuffles amount bands and places one band per block. The shifted layout rotates the same rows by 341 positions so each block straddles two bands. The shuffled layout shuffles individual rows. Each block holds 1,024 data records.

For each layout, the sequential fallback and the indexed lookup read byte-identical CSV copies and apply the same half-open decimal ranges. The benchmark verifies every matching ID in source order before timing and checks the count and an order-sensitive fingerprint on every run. Each measurement opens, drains, and disposes a reader. The runner alternates the order of the two paths across 15 warm-cache pairs after five warm-up pairs. It reports the median elapsed time and managed bytes allocated on the calling thread. Build times are medians of five sidecar replacements after two warm-up builds.

## Results

Measured on Windows x64 with .NET 10.0.15 at commit `c86d44c`. Each CSV was 11,723,530 bytes. Its sidecar was 11,620 bytes and took 97.606–100.070 ms to build.

| Workload | Matches | Candidate blocks | Sequential median | Zone-map median | Speedup | Sequential bytes | Zone-map bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Narrow range, aligned clusters | 1,024 | 1 / 256 | 96.173 ms | 0.846 ms | 113.7× | 8,906,568 | 71,496 |
| Narrow range, shifted clusters | 1,024 | 72 / 256 | 93.935 ms | 26.507 ms | 3.5× | 8,906,144 | 2,631,080 |
| Range matching 25% of clustered rows | 65,536 | 64 / 256 | 95.878 ms | 26.074 ms | 3.7× | 8,906,312 | 2,383,400 |
| Range outside all values | 0 | 0 / 256 | 93.231 ms | 0.257 ms | 362.5× | 8,906,144 | 31,336 |
| Narrow range, shuffled rows | 1,024 | 256 / 256 | 95.093 ms | 94.878 ms | ≈1× | 8,906,144 | 8,929,760 |
| Range matching every row | 262,144 | 256 / 256 | 102.624 ms | 103.223 ms | ≈1× | 8,906,392 | 8,929,760 |

Two selective lookups recovered the build cost in the clustered and shifted cases. The aligned layout shows the benefit when the bounds identify a single block. Shifting the same rows kept 72 blocks eligible for the same 1,024 matches. With all 256 blocks eligible, lookup time was about the same as a sequential scan.
