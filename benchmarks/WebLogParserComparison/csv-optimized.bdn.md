```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method          | Parser            | Mean     | Error     | StdDev   | Gen0     | Allocated |
|---------------- |------------------ |---------:|----------:|---------:|---------:|----------:|
| **ReadThreeValues** | **CsvChars**          | **45.40 ms** | **17.468 ms** | **0.957 ms** |        **-** |   **4.17 MB** |
| **ReadThreeValues** | **CsvUtf8**           | **28.66 ms** |  **7.529 ms** | **0.413 ms** | **187.5000** |   **4.16 MB** |
| **ReadThreeValues** | **FixedWidth**        | **12.97 ms** |  **7.807 ms** | **0.428 ms** | **218.7500** |   **4.17 MB** |
| **ReadThreeValues** | **Ndjson**            | **48.76 ms** |  **3.867 ms** | **0.212 ms** | **200.0000** |    **4.4 MB** |
| **ReadThreeValues** | **Json**              | **50.30 ms** | **14.531 ms** | **0.797 ms** | **181.8182** |    **4.4 MB** |
| **ReadThreeValues** | **Logfmt**            | **22.67 ms** | **22.045 ms** | **1.208 ms** | **218.7500** |   **4.38 MB** |
| **ReadThreeValues** | **Ltsv**              | **12.21 ms** |  **6.248 ms** | **0.342 ms** | **234.3750** |   **4.36 MB** |
| **ReadThreeValues** | **CommonLog**         | **16.24 ms** |  **1.286 ms** | **0.070 ms** | **234.3750** |   **4.29 MB** |
| **ReadThreeValues** | **CombinedLog**       | **16.61 ms** | **18.900 ms** | **1.036 ms** | **218.7500** |   **4.35 MB** |
| **ReadThreeValues** | **W3c**               | **10.09 ms** |  **0.880 ms** | **0.048 ms** | **375.0000** |   **6.93 MB** |
| **ReadThreeValues** | **JsonProjected**     | **19.69 ms** |  **3.600 ms** | **0.197 ms** | **218.7500** |    **4.4 MB** |
| **ReadThreeValues** | **NdjsonProjected**   | **31.99 ms** | **75.615 ms** | **4.145 ms** | **218.7500** |    **4.4 MB** |
| **ReadThreeValues** | **CsvCharsOptimized** | **20.12 ms** |  **0.202 ms** | **0.011 ms** | **218.7500** |   **4.17 MB** |
| **ReadThreeValues** | **CsvUtf8Optimized**  | **13.74 ms** |  **2.286 ms** | **0.125 ms** | **218.7500** |   **4.16 MB** |
