```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method          | Ndjson | Projection | EscapedTimestamp | Mean     | Error     | StdDev   | Gen0     | Allocated |
|---------------- |------- |----------- |----------------- |---------:|----------:|---------:|---------:|----------:|
| **ReadThreeValues** | **False**  | **False**      | **False**            | **30.97 ms** |  **4.238 ms** | **0.232 ms** | **187.5000** |   **4.39 MB** |
| **ReadThreeValues** | **False**  | **False**      | **True**             | **31.37 ms** | **12.384 ms** | **0.679 ms** | **437.5000** |   **8.21 MB** |
| **ReadThreeValues** | **False**  | **True**       | **False**            | **16.47 ms** |  **1.750 ms** | **0.096 ms** | **218.7500** |   **4.39 MB** |
| **ReadThreeValues** | **False**  | **True**       | **True**             | **18.15 ms** |  **4.856 ms** | **0.266 ms** | **218.7500** |    **4.4 MB** |
| **ReadThreeValues** | **True**   | **False**      | **False**            | **30.56 ms** |  **3.469 ms** | **0.190 ms** | **218.7500** |   **4.39 MB** |
| **ReadThreeValues** | **True**   | **False**      | **True**             | **32.71 ms** |  **4.674 ms** | **0.256 ms** | **437.5000** |   **8.22 MB** |
| **ReadThreeValues** | **True**   | **True**       | **False**            | **24.27 ms** |  **0.607 ms** | **0.033 ms** | **218.7500** |   **4.39 MB** |
| **ReadThreeValues** | **True**   | **True**       | **True**             | **27.34 ms** |  **0.899 ms** | **0.049 ms** | **218.7500** |    **4.4 MB** |
