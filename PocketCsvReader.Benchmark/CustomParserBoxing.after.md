```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9974)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host] : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3

PowerPlanMode=00000000-0000-0000-0000-000000000000  Toolchain=InProcessEmitToolchain  IterationCount=10
IterationTime=250ms  WarmupCount=5

```
| Method             | UseBuilder | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------- |----------- |---------:|---------:|---------:|-------:|----------:|
| **TypedInt32**         | **False**      | **34.88 ns** | **0.330 ns** | **0.196 ns** | **0.0021** |      **40 B** |
| TypedInt64         | False      | 37.44 ns | 0.321 ns | 0.212 ns | 0.0020 |      40 B |
| TypedStruct        | False      | 39.21 ns | 0.386 ns | 0.255 ns | 0.0021 |      40 B |
| BoxedInt32         | False      | 38.32 ns | 0.630 ns | 0.417 ns | 0.0033 |      64 B |
| StringInputControl | False      | 47.21 ns | 0.899 ns | 0.595 ns | 0.0021 |      40 B |
| SpanControl        | False      | 36.49 ns | 0.107 ns | 0.070 ns |      - |         - |
| **TypedInt32**         | **True**       | **34.55 ns** | **0.365 ns** | **0.242 ns** | **0.0020** |      **40 B** |
| TypedInt64         | True       | 37.23 ns | 0.112 ns | 0.074 ns | 0.0020 |      40 B |
| TypedStruct        | True       | 38.93 ns | 0.317 ns | 0.188 ns | 0.0021 |      40 B |
| BoxedInt32         | True       | 38.77 ns | 0.952 ns | 0.629 ns | 0.0033 |      64 B |
| StringInputControl | True       | 47.54 ns | 0.499 ns | 0.330 ns | 0.0020 |      40 B |
| SpanControl        | True       | 37.63 ns | 0.159 ns | 0.105 ns |      - |         - |
