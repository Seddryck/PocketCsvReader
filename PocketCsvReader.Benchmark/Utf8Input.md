# UTF-8 input comparison

Run the two parsing representations against the same in-memory input:

```shell
dotnet run --project PocketCsvReader.Benchmark -c Release -f net10.0 -- --filter '*Utf8Input*' --warmupCount 3 --iterationCount 5 --launchCount 1
```

Each operation reads 2,000 records. Narrow records contain three fields; wide records contain 24. Numeric access calls `GetInt32` on every field, string access calls `GetString` on every field, and skipped-column cases advance through the records without requesting values. Dataset creation is outside measurement. MemoryDiagnoser reports managed allocations per complete operation. These measurements cover parsing and access, not disk throughput.

Measured on 2026-10-22 with BenchmarkDotNet 0.15.8, .NET 10.0.12, Windows 11 x64, and Intel Core Ultra 9 275HX. The original UTF-8 implementation is commit `853d93e`; its baseline was rerun before optimization. String scenarios were rerun after the final compatibility guard with `--filter '*Utf8Input*Strings*'`. The remaining scenarios come from the full optimized run.

| Scenario | Character control mean | Original UTF-8 mean | Optimized UTF-8 mean |
| --- | ---: | ---: | ---: |
| ASCII numeric, narrow | 606.3 μs | 421.3 μs | 469.2 μs |
| ASCII, wide, skipped | 1,909.0 μs | 2,226.1 μs | 1,564.4 μs |
| ASCII strings, wide | 2,948.6 μs | 2,677.9 μs | 2,519.5 μs |
| Multiline strings, narrow | 653.9 μs | 731.6 μs | 528.5 μs |
| Unicode strings, narrow | 455.1 μs | 764.5 μs | 454.0 μs |
| Unicode, wide, skipped | 2,149.2 μs | 3,432.0 μs | 1,894.5 μs |

| Scenario | Character allocated | Original UTF-8 allocated | Optimized UTF-8 allocated |
| --- | ---: | ---: | ---: |
| ASCII numeric, narrow | 608.52 KB | 534.80 KB | 535.44 KB |
| ASCII, wide, skipped | 3,425.15 KB | 2,836.46 KB | 2,837.10 KB |
| ASCII strings, wide | 4,935.84 KB | 6,269.33 KB | 4,337.32 KB |
| Multiline strings, narrow | 997.60 KB | 1,192.43 KB | 816.73 KB |
| Unicode strings, narrow | 796.12 KB | 1,004.91 KB | 724.26 KB |
| Unicode, wide, skipped | 3,425.22 KB | 2,836.46 KB | 2,837.10 KB |

The string-heavy cases allocated 28–32% less than the original byte path. Unicode string time decreased about 41%, multiline string time about 28%, and wide ASCII string time about 6%. In the final string run, Unicode reached parity with character parsing; wide ASCII and multiline strings were faster. Numeric timing varied between runs and the representative optimized sample above is slower than its original byte baseline, while still faster than the character control. These short comparisons use one launch and five requested measured iterations; machine conditions and character controls varied between runs. Small timing differences need longer runs, and these results do not establish a universal speedup. Character parsing remains the default.

The original string path decoded a field into a cached string, then copied it through `StringMapper` into another string. The optimized path uses lazily rented, reusable character slots and per-field decoded lengths, eliminating the intermediate string and per-record string-cache array. Eligible ASCII `GetString` calls decode directly into the returned string; multibyte text uses the pooled cache to avoid a separate UTF-8 character-count pass. Custom pools, sequence substitutions, escaping, and derived-reader overrides keep their existing processing.

The scanner also uses `SearchValues<byte>` to copy ordinary payload runs up to the next CSV syntax byte. The shared state machine still handles every syntax byte and the first payload byte after a state transition. This avoids dispatching every byte of multibyte Unicode text through the parser. Whole-record character projections translate field offsets in input order, avoiding repeated prefix scans for wide object mappings.

Correctness coverage includes generated quoted records, malformed quotes, partial/custom terminators, escapes, invalid UTF-8 replacement, reverse/repeated field access, buffer growth, custom pools, derived readers, and Unicode character offsets. This benchmark does not separately measure custom-pool or whole-record mapper workloads.

The implementation uses pooled contiguous buffers. No pipeline or external UTF-8 line-reader prototype was introduced, so these results compare only the two shipped parsers.

These measurements precede the shared character-core refactor. Its separate [character parser comparison](CharacterRecordParsing.md) reports the subsequent character-path changes; the UTF-8 scanner remains separate.
