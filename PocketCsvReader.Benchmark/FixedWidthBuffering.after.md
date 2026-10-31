```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method    | Width | Mean        | Error        | StdDev     | Gen0   | Gen1   | Allocated |
|---------- |------ |------------:|-------------:|-----------:|-------:|-------:|----------:|
| **Rows**      | **32**    |    **27.10 μs** |     **5.372 μs** |   **0.294 μs** | **2.4719** | **0.0305** |  **45.91 KB** |
| Typed     | 32    |    36.80 μs |    30.636 μs |   1.679 μs | 2.5024 |      - |   46.4 KB |
| RowsAsync | 32    |    48.28 μs |     5.766 μs |   0.316 μs | 2.4414 |      - |  45.98 KB |
| **Rows**      | **8192**  | **5,288.32 μs** | **1,151.450 μs** |  **63.115 μs** |      **-** |      **-** |  **45.91 KB** |
| Typed     | 8192  | 5,442.25 μs | 5,002.641 μs | 274.212 μs |      - |      - |   46.4 KB |
| RowsAsync | 8192  | 8,017.27 μs | 1,449.934 μs |  79.476 μs |      - |      - |  45.98 KB |
