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
| **TypedInt32**         | **False**      | **44.65 ns** | **0.456 ns** | **0.271 ns** | **0.0033** |      **64 B** |
| TypedInt64         | False      | 40.64 ns | 0.497 ns | 0.329 ns | 0.0034 |      64 B |
| TypedStruct        | False      | 43.15 ns | 0.350 ns | 0.208 ns | 0.0032 |      64 B |
| BoxedInt32         | False      | 42.74 ns | 0.606 ns | 0.401 ns | 0.0045 |      88 B |
| StringInputControl | False      | 50.01 ns | 0.626 ns | 0.414 ns | 0.0021 |      40 B |
| SpanControl        | False      | 37.29 ns | 0.056 ns | 0.029 ns |      - |         - |
| **TypedInt32**         | **True**       | **40.65 ns** | **0.467 ns** | **0.278 ns** | **0.0034** |      **64 B** |
| TypedInt64         | True       | 40.52 ns | 0.603 ns | 0.399 ns | 0.0033 |      64 B |
| TypedStruct        | True       | 43.11 ns | 0.684 ns | 0.452 ns | 0.0034 |      64 B |
| BoxedInt32         | True       | 44.16 ns | 0.684 ns | 0.453 ns | 0.0047 |      88 B |
| StringInputControl | True       | 50.09 ns | 0.613 ns | 0.365 ns | 0.0021 |      40 B |
| SpanControl        | True       | 35.86 ns | 0.191 ns | 0.114 ns |      - |         - |
