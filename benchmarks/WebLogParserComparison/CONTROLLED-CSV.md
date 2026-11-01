# Controlled CSV UTF-8 follow-up for PR #393

The previously reported CPU regression did **not reproduce** under matched controls.
The point estimates favor PR #393, but four-pair uncertainty includes parity and
small regressions; this is not a proof of a universal speedup or zero regression.
The roughly 16% slowdown suggested by comparing separate historical sessions is
not supported by these measurements.

## Controls

Each experiment uses four independent matched pairs of fresh processes. Order is
before/after, after/before, before/after, after/before. Both variants:

- run the same harness source and verified identical hot-loop/projection IL;
- consume identical SHA-256-verified UTF-8 bytes (4,409,248 bytes; 50,000 records);
- validate every returned tuple during setup and check every measured checksum;
- run Release on .NET 10.0.14, x64, workstation GC, default TieredPGO settings;
- pin **only the worker process** to logical CPU 2 (affinity `0x4`);
- warm up for five seconds, then measure five batches of at least two seconds;
- exclude full GCs before samples, generation, setup validation, and output logging;
- measure process CPU, elapsed time, calling-thread allocations and GC counts.

No builds or tests run during the measurements. No machine power plan or global
configuration is changed. Affinity, runtime, GC mode, TieredPGO setting, input and
IL hashes match within each experiment. Parser assembly module IDs are constant
within variants and distinct between variants. Raw metadata verifies that the
same main/original/fixed assemblies are used across experiments.

Hardware: Intel Core Ultra 9 275HX, Windows 11; SDK 11.0.100-rc.2.26504.105.
All measurements were taken on 2026-11-01. Pinning reduces migration and P/E-core
variation, but does not fix CPU frequency or remove other host load. Worker
replications, rather than individual within-process batches, are the paired units.

## Results per 50,000 records

| Matched experiment | Before CPU (ms) | After CPU (ms) | Geometric after/before | Approx. paired 95% interval |
|---|---:|---:|---:|---:|
| Main `b359baf` vs original PR `93a566a` | 17.155 | 16.473 | 0.960 | 0.884–1.043 |
| Main `b359baf` vs updated PR | 16.707 | 16.303 | 0.976 | 0.936–1.017 |
| Original PR vs updated PR, direct | 16.407 | 15.877 | 0.968 | 0.906–1.034 |

CPU values average the four worker means; ratios average paired log ratios.
Intervals use Student-t on the four paired log ratios (df=3, critical value
3.182446), then exponentiate. Small sample size and host/JIT variation limit the
inference. These are separate experiments, so do not subtract means across rows
to infer the effect of the overhead fix.

The original PR is about 4% faster at the point estimate; the updated PR is about
2.4% faster than main in its own matched comparison. The direct overhead comparison
has a 3.2% improvement point estimate, but its interval includes parity. We removed
redundant work without claiming a separately proven small CPU speedup.

Allocation is consistent across all repetitions:

| Version | Allocated bytes/input | Decimal MB/input |
|---|---:|---:|
| Main | 29,165,608 | 29.166 |
| Original PR | approximately 4,365,890 | 4.366 |
| Updated PR | approximately 4,365,898 | 4.366 |

The 85% reduction remains. The extra approximately eight bytes in the update are
one cached interface reference per reader/input, not a per-row allocation.

## Overhead removed without weakening ownership

The original `BaseDataReader.ReadSource` and `ReadSourceAsync` performed an
interface/type test and queried `SupportsCurrentRecord` on every source read.
That capability is stable for a source's lifetime. It is now resolved once in
`CompleteInitialization` and the selected interface is cached.

The asynchronous borrowed path now returns its source's `ValueTask` directly,
rather than adding a forwarding async state machine. Legacy public sources still
use the public fallback helper and their virtual read overrides. No changes were
made to borrowed field storage, owned public snapshots, mapper retention, or
legacy wrapper materialization.

`SourceCapability_IsResolvedOnceForMixedReads` checks one eligibility query after
initialization and mixed sync/async/EOF reads. Existing tests still check metadata
retention, wrapper absence, allocation budgets, custom overrides, and async-only I/O.
The parser-design skill now documents this initialization-time selection rule.

## Raw evidence

- `controlled-original.csv/.json/.pairs.csv`: main versus original PR.
- `controlled-final.csv/.json/.pairs.csv`: main versus updated implementation.
- `controlled-overhead.csv/.json/.pairs.csv`: original versus updated implementation.
- `controlled-summary.json`: aggregate values, paired intervals and assembly IDs.

Each CSV contains 40 batch samples; each JSON contains eight worker results with
provenance and controls. The direct overhead experiment uses **byte-identical
harness binaries** (SHA-256
`E22A4B81E10A76074BA57329557637F36799BF188D23C47C3E319D7EECEA4D69`),
with only the `PocketCsvReader*.dll` parser dependencies changed to the original
PR binaries. Those original module IDs match the first experiment. The other
comparisons compile identical harness source against different parser checkouts;
their hot-loop/projection IL hashes are verified equal.

## Reproduce

Use the same logical CPU and settings for all workers. The controlled worker is
Windows-specific; choose a core available to the process. From the repository root,
create a baseline checkout if one is not already available:

```powershell
git worktree add --detach .worktrees/csv-cpu-baseline b359baf
$baseline = (Resolve-Path .worktrees/csv-cpu-baseline).Path
$updated = $PWD.Path
$beforeOut = Join-Path $updated 'artifacts/controlled/before'
$afterOut = Join-Path $updated 'artifacts/controlled/after'
dotnet build benchmarks/WebLogParserComparison/WebLogParserComparison.csproj -c Release -f net10.0 "-p:ParserSourceRoot=$baseline" -o $beforeOut
dotnet build benchmarks/WebLogParserComparison/WebLogParserComparison.csproj -c Release -f net10.0 "-p:ParserSourceRoot=$updated" -o $afterOut
./benchmarks/WebLogParserComparison/Run-ControlledCsv.ps1 -BeforeAssembly "$beforeOut/WebLogParserComparison.dll" -AfterAssembly "$afterOut/WebLogParserComparison.dll" -OutputPrefix 'artifacts/controlled/reproduction' -AfterLabel 'updated-pr' -Core 2
```

For the original PR comparison, build the same current harness against a separate
checkout of `93a566a`. Keep each variant's output directory immutable while workers
run. The runner fails on mismatched input, IL, runtime, affinity or parser identity.
Its defaults implement the measured four-pair, five-batch, five-second warmup,
two-second sample design. Do not run builds/tests concurrently with it.

## Validation of the update

- Final Release tests on each of .NET 8, 9 and 10: Core 1,114; JSON 1,155;
  NDJSON 310; KeyValue 77; FixedWidth 68; WebLogs 146; SecurityLogs 52.
  **8,766 passing executions**, with no failures/skips.
- Full solution Release build passed (four warnings in existing test/profiler code).
- Benchmark project built for .NET 8/9/10 without warnings/errors; skill quick
  validation and `git diff --check` passed.
- Allocation guard passed (18 reviewed occurrences); guard self-tests passed 30 checks.
- Each of 24 fresh worker processes checked all 50,000 projected rows and every
  measured invocation's checksum. All three experiments passed their controls.
- Not run: the full repository suite beyond those seven projects, Arrow/Chrononuensis
  tests, controlled CPU comparisons for other parser modes, benchmarks on .NET 8/9,
  other operating systems, or another twelve-parser matrix. The older full matrix
  in the main README is explicitly historical; this follow-up resolves CSV UTF-8.
