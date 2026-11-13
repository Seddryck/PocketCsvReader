# CSV index comparison

From the repository root, run the benchmark in Release with the .NET 10 SDK and runtime.

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --csv-index-comparison
```

The runner generates 196,608 orders across 12 CSV files. Each file has four sorted day groups of 4,096 records. Within each day, amounts are clustered into eight 512-record blocks. The selective customer appears in two files. The shared customer appears in every file. The files include quoted multiline fields. The fixture and runner are in [CsvIndexComparison.cs](CsvIndexComparison.cs).

Each configuration gets byte-identical CSV files in its own directory. The scan directory contains zero sidecars. Single-index directories contain one sidecar type each. The two chains use Bloom, zone map, and sparse, or all six indexes. Each query registers the same six predicates. An unavailable sidecar uses that index's optional exact scan fallback, so every configuration applies the same predicates to its candidates. This includes the normal filesystem checks for optional sidecars.

| Query | Customer | Day | Amount | Category | Tags | Warehouse |
| --- | --- | ---: | --- | --- | --- | ---: |
| Selective | `target` | 3 | [300, 400) | EU and (shipped or delivered) | Both urgent and refund | 17 |
| Broader | `shared` | 3 | [100, 900) | EU and any of the three generated statuses | Urgent or routine | 23 |
| Absent customer | `missing` | 3 | [300, 400) | EU and (shipped or delivered) | Both urgent and refund | 17 |

The runner verifies the full sequence of matching IDs against the generated orders before timing. It checks the match count and an order-sensitive fingerprint on every warm-up and measured run. Each lookup measurement opens, drains, and disposes a reader. Query construction happens before timing. After three warm-ups per configuration, the runner rotates through all nine configurations for 18 warm-cache samples each. It reports median elapsed time and managed bytes allocated on the calling thread. Sidecar build time is the median of three rebuilds after one warm-up build, summed across the 12 source files by timing each full build operation. The chain build measurement includes all sidecar types it uses.

## Results

Measured on an Intel Core Ultra 9 275HX with Windows x64 and .NET 10.0.15. The 12 source CSV files totaled 12,718,834 bytes.

### Lookup time

Each cell shows median elapsed time and speed relative to the scan for that query. The selective query returned 8 rows, the broader query returned 23,688, and the absent-customer query returned zero.

| Configuration | Selective | Broader | Absent customer |
| --- | ---: | ---: | ---: |
| Scan | 93.937 ms | 103.185 ms | 90.427 ms |
| Bloom | 16.870 ms (5.57×) | 106.908 ms (0.97×) | 0.474 ms (191×) |
| Zone map | 15.537 ms (6.05×) | 105.821 ms (0.98×) | 13.829 ms (6.54×) |
| Bitmap | 118.658 ms (0.79×) | 93.816 ms (1.10×) | 96.929 ms (0.93×) |
| Inverted | 45.527 ms (2.06×) | 124.184 ms (0.83×) | 39.942 ms (2.26×) |
| Hash | 45.502 ms (2.06×) | 111.533 ms (0.93×) | 35.492 ms (2.55×) |
| Sparse | 30.614 ms (3.07×) | **39.609 ms (2.61×)** | 28.163 ms (3.21×) |
| Bloom + zone map + sparse | **2.138 ms (43.94×)** | 41.236 ms (2.50×) | 0.474 ms (191×) |
| All six | 6.030 ms (15.58×) | 76.981 ms (1.34×) | 0.437 ms (207×) |

### Build cost and allocations

| Configuration | Sidecar bytes | Median build time | Selective allocated bytes | Broader allocated bytes | Absent allocated bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan | 0 | — | 8,055,080 | 21,686,912 | 8,055,204 |
| Bloom | 9,984 | 325.152 ms | 1,411,144 | 21,744,576 | 72,680 |
| Zone map | 9,264 | 115.638 ms | 2,663,832 | 21,799,408 | 2,664,000 |
| Bitmap | 2,546,040 | 134.631 ms | 41,050,544 | 27,175,360 | 41,050,544 |
| Inverted | 4,720,824 | 233.529 ms | 14,835,536 | 36,238,968 | 14,834,016 |
| Hash | 4,719,792 | 194.292 ms | 15,416,832 | 32,354,936 | 15,416,832 |
| Sparse | 13,881 | 109.891 ms | 4,630,464 | 18,262,624 | 4,630,352 |
| Bloom + zone map + sparse | 33,129 | 332.703 ms | 470,296 | 18,391,912 | 73,064 |
| All six | 12,019,785 | 745.567 ms | 2,023,872 | 33,568,356 | 72,680 |

The three-index chain fits the selective layout. Bloom can reject ten of the twelve files, the zone map keeps one amount block per day in the surviving files, and sparse seeking and ordered stop limit the read to the requested day. The chain was 2.8× faster than the six-index chain and used about 0.26% of the source CSV size in sidecars. The broader query spans all files and amount blocks, so sparse alone had the lowest median. All three Bloom configurations rejected the absent customer at the file stage and took about half a millisecond.

The bitmap-only lookup illustrates the cost of a common, scattered category. Its sidecars yield many eligible records, and the remaining five predicates still have to be checked. This workload's layout and selectivity drive these results. See the separate [zone-map layout benchmark](ZoneMapBenchmark.md) for measurements with different clustering.

## 100× dataset

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --csv-index-comparison-100x
```

This run has 19,660,800 rows across 120 files of 163,840 rows each. Each sorted day group repeats its original 4,096-row pattern ten times, and each of the 12 file layouts occurs ten times. Order IDs are unique across the dataset. The same predicates return 800 selective matches, 2,368,800 broader matches, and zero matches for the absent customer. Bloom can still reject five out of six files for the selective customer, and zone maps use the same 512-row blocks.

The larger run validates the exact ordered IDs once per configuration, then takes the median of three warm-cache lookups. Each round starts at a different point in the nine-configuration sequence. Build time is one timed build from scratch per configuration. The runner generates data one file at a time, keeps the matching IDs for the current query in memory, and removes its temporary CSVs and sidecars when it exits. Allow about 15 GB of temporary space for the nine source copies and their sidecars.

Measured on the same Windows x64 host with .NET 10.0.15. The 120 source CSVs totaled 1,311,142,250 bytes.

### Lookup time at 100×

Each cell shows the median of three samples and speed relative to the scan for that query.

| Configuration | Selective | Broader | Absent customer |
| --- | ---: | ---: | ---: |
| Scan | 9.421 s | 9.868 s | 9.532 s |
| Bloom | 1.564 s (6.02×) | 9.921 s (0.99×) | 0.009 s (1051×) |
| Zone map | 1.302 s (7.24×) | 9.986 s (0.99×) | 1.304 s (7.31×) |
| Bitmap | 11.607 s (0.81×) | 8.500 s (1.16×) | 11.602 s (0.82×) |
| Inverted | 4.519 s (2.08×) | 12.400 s (0.80×) | 4.456 s (2.14×) |
| Hash | 4.466 s (2.11×) | 10.141 s (0.97×) | 4.410 s (2.16×) |
| Sparse | 2.560 s (3.68×) | 3.169 s (3.11×) | 2.591 s (3.68×) |
| Bloom + zone map + sparse | 0.081 s (116×) | 3.191 s (3.09×) | 0.009 s (1037×) |
| All six | 0.504 s (18.70×) | 6.671 s (1.48×) | 0.009 s (1069×) |

### Build cost and allocations at 100×

| Configuration | Sidecar bytes | Build time | Selective allocated bytes | Broader allocated bytes | Absent allocated bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scan | 0 | — | 781,334,880 | 2,144,662,240 | 781,335,104 |
| Bloom | 99,840 | 11.073 s | 130,742,584 | 2,145,260,184 | 722,632 |
| Zone map | 818,400 | 10.291 s | 237,025,312 | 2,147,485,240 | 237,025,312 |
| Bitmap | 254,385,840 | 12.149 s | 4,063,516,152 | 2,697,263,576 | 4,063,471,456 |
| Inverted | 471,881,520 | 13.129 s | 1,372,538,720 | 5,120,837,336 | 1,372,545,680 |
| Hash | 471,871,200 | 12.148 s | 1,501,340,984 | 3,199,685,384 | 1,501,344,568 |
| Sparse | 868,178 | 10.512 s | 403,348,840 | 1,766,707,656 | 403,348,840 |
| Bloom + zone map + sparse | 1,786,418 | 31.363 s | 32,435,904 | 1,769,661,576 | 726,408 |
| All six | 1,199,924,978 | 70.229 s | 169,220,960 | 4,771,409,304 | 722,408 |

Bloom + zone map + sparse completed the selective query in 81 ms with about 1.79 MB of sidecars. The six-index chain took 504 ms and used about 1.20 GB of sidecars. Sparse alone led the broader query, with the three-index chain within 22 ms. For the absent customer, all three Bloom configurations completed in about 9 ms after rejecting the files at the Bloom stage.
