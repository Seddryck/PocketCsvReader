```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0
IterationCount=3  LaunchCount=1  WarmupCount=3

```
| Method | Format      | PathLength | Mean        | Error        | StdDev     | Gen0     | Gen1    | Allocated  |
|------- |------------ |----------- |------------:|-------------:|-----------:|---------:|--------:|-----------:|
| **Rows**   | **Common**      | **32**         |    **76.29 μs** |     **3.225 μs** |   **0.177 μs** |   **0.3662** |       **-** |    **7.43 KB** |
| Typed  | Common      | 32         |   344.97 μs |    36.649 μs |   2.009 μs |   9.7656 |       - |   187.6 KB |
| **Rows**   | **Common**      | **8192**       | **1,280.19 μs** |    **27.033 μs** |   **1.482 μs** |   **1.9531** |       **-** |   **66.14 KB** |
| Typed  | Common      | 8192       | 2,116.97 μs | 2,481.025 μs | 135.993 μs | 453.1250 | 62.5000 | 8406.31 KB |
| **Rows**   | **W3cExtended** | **32**         |    **62.80 μs** |     **3.515 μs** |   **0.193 μs** |   **0.3662** |       **-** |    **7.62 KB** |
| Typed  | W3cExtended | 32         |   302.86 μs |    24.926 μs |   1.366 μs |   9.7656 |       - |  187.79 KB |
| **Rows**   | **W3cExtended** | **8192**       | **1,584.09 μs** |    **31.826 μs** |   **1.745 μs** |   **1.9531** |       **-** |   **66.56 KB** |
| Typed  | W3cExtended | 8192       | 2,491.61 μs |   958.813 μs |  52.556 μs | 453.1250 | 62.5000 | 8406.73 KB |
