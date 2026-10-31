```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method    | Width | Mean        | Error         | StdDev     | Gen0   | Gen1   | Allocated |
|---------- |------ |------------:|--------------:|-----------:|-------:|-------:|----------:|
| **Rows**      | **32**    |    **37.06 μs** |      **1.541 μs** |   **0.084 μs** | **2.4719** | **0.0305** |  **45.91 KB** |
| Typed     | 32    |    41.30 μs |    135.393 μs |   7.421 μs | 2.5024 |      - |   46.4 KB |
| RowsAsync | 32    |    47.66 μs |      2.729 μs |   0.150 μs | 2.4414 |      - |  45.98 KB |
| **Rows**      | **8192**  | **6,332.92 μs** | **11,816.463 μs** | **647.700 μs** |      **-** |      **-** |  **45.91 KB** |
| Typed     | 8192  | 5,685.29 μs |  4,138.953 μs | 226.870 μs |      - |      - |   46.4 KB |
| RowsAsync | 8192  | 5,693.02 μs |  7,444.799 μs | 408.075 μs |      - |      - |  45.98 KB |
