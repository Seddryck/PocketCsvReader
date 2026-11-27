# Labeled reader throughput

The baseline is commit `c3df1a0`. Both revisions ran the same benchmark executable on Windows x64 with .NET 10 and an Intel Core Ultra 9 275HX. Each mode ran in a fresh process. The harness warms each case for 600 ms, then reports the median of five samples lasting at least 500 ms each. CPU time is process CPU time. Allocated bytes use the synchronous reader thread. The CSV files also include elapsed time and checksums, which matched across revisions.

Each enrollment pass reads 50,000 UTF-8 records with the same seven fields as the RealCsv benchmark. **RealCsv** shuffles 3,500 distinct seeded name pairs and includes accented text and quoted university names. **Clustered** repeats the string values in runs of 64. **MostlyUnique** gives each row distinct strings. The typed pass reads four strings, a decimal, a short, and a DateOnly. The scan pass counts rows without accessing fields. The separate line-reader mode scans 50,000 Common Log or Logfmt records.

## Scan only

| Format | Distribution | CPU ms before → after | Allocated bytes before → after |
| --- | --- | ---: | ---: |
| JSON | RealCsv | 29.51 → 31.25 | 23,928 → 23,960 |
| NDJSON | RealCsv | 30.33 → 29.41 | 23,856 → 23,920 |
| JSON | Clustered | 26.32 → 28.65 | 23,808 → 23,840 |
| NDJSON | Clustered | 28.65 → 29.41 | 23,856 → 23,920 |
| JSON | MostlyUnique | 29.51 → 32.23 | 26,928 → 26,960 |
| NDJSON | MostlyUnique | 32.23 → 31.25 | 26,856 → 26,920 |
| CommonLog | Requests | 9.80 → 9.92 | 14,248 → 14,280 |
| Logfmt | Requests | 6.39 → 7.35 | 13,032 → 13,064 |

## Typed enrollment rows

The three values in each column are baseline, implementation with the hint disabled, and implementation with `PoolStringLastValueHint` enabled.

| Format | Distribution | CPU ms | Allocated MiB |
| --- | --- | ---: | ---: |
| JSON | RealCsv | 45.57 → 48.30 → 46.88 | 8.75 → 8.75 → 8.04 |
| NDJSON | RealCsv | 49.72 → 49.72 → 48.30 | 8.75 → 8.75 → 8.04 |
| JSON | Clustered | 42.97 → 45.45 → 44.27 | 8.75 → 8.75 → 0.16 |
| NDJSON | Clustered | 46.88 → 50.00 → 44.27 | 8.75 → 8.75 → 0.16 |
| JSON | MostlyUnique | 48.30 → 53.13 → 48.30 | 11.68 → 11.68 → 11.68 |
| NDJSON | MostlyUnique | 51.56 → 51.56 → 49.72 | 11.68 → 11.68 → 11.68 |

The hint reduced typed allocations by about 8% for the shuffled RealCsv workload and 98% for clustered repeats. The mostly unique workload showed essentially the same allocation volume. RealCsv scan CPU fell 3% for NDJSON and rose 6% for JSON. Common Log was within 2% of baseline and Logfmt scan CPU rose 15%. The Logfmt baseline had a larger gap between elapsed time and CPU time than other cases, so its elapsed-time comparison is less reliable.

[Baseline measurements](LabeledReaderThroughput.before.csv) · [Implementation measurements](LabeledReaderThroughput.after.csv)

## Reproduce

Run the following from the implementation worktree. Build the baseline libraries from `c3df1a0` and copy only the benchmark executable assembly to use an identical workload on both revisions.

```powershell
dotnet build PocketCsvReader.Benchmark -c Release -f net10.0
git worktree add --detach .worktrees/labeled-reader-benchmark-baseline c3df1a0
dotnet build .worktrees/labeled-reader-benchmark-baseline/PocketCsvReader.Benchmark -c Release -f net10.0
$after = 'PocketCsvReader.Benchmark/bin/Release/net10.0/PocketCsvReader.Benchmark.dll'
$before = '.worktrees/labeled-reader-benchmark-baseline/' + $after
Copy-Item -LiteralPath $after -Destination $before
foreach ($mode in 'standard', 'pool', 'lines') {
    dotnet $before --labeled-reader-throughput $mode
    dotnet $after --labeled-reader-throughput $mode
}
```
