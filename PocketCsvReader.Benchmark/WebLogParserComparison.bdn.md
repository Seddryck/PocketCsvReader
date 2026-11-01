```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method          | Parser      | Mean      | Error     | StdDev    | Gen0      | Gen1    | Allocated |
|---------------- |------------ |----------:|----------:|----------:|----------:|--------:|----------:|
| **ReadThreeValues** | **CsvChars**    | **26.275 ms** | **3.2246 ms** | **0.1768 ms** | **1531.2500** | **31.2500** |  **27.82 MB** |
| **ReadThreeValues** | **CsvUtf8**     | **17.155 ms** | **0.8875 ms** | **0.0486 ms** | **1531.2500** |       **-** |  **27.81 MB** |
| **ReadThreeValues** | **FixedWidth**  |  **7.795 ms** | **0.6659 ms** | **0.0365 ms** |  **437.5000** |       **-** |   **7.98 MB** |
| **ReadThreeValues** | **Ndjson**      | **41.540 ms** | **8.8695 ms** | **0.4862 ms** | **4083.3333** |       **-** |  **73.83 MB** |
| **ReadThreeValues** | **Json**        | **40.670 ms** | **8.0396 ms** | **0.4407 ms** | **4076.9231** |       **-** |  **73.83 MB** |
| **ReadThreeValues** | **Logfmt**      | **15.759 ms** | **5.8369 ms** | **0.3199 ms** |  **218.7500** |       **-** |   **4.38 MB** |
| **ReadThreeValues** | **Ltsv**        |  **9.646 ms** | **2.2532 ms** | **0.1235 ms** |  **234.3750** |       **-** |   **4.36 MB** |
| **ReadThreeValues** | **CommonLog**   | **10.751 ms** | **2.2753 ms** | **0.1247 ms** |  **234.3750** |       **-** |   **4.29 MB** |
| **ReadThreeValues** | **CombinedLog** | **10.819 ms** | **0.8068 ms** | **0.0442 ms** |  **234.3750** |       **-** |   **4.35 MB** |
| **ReadThreeValues** | **W3c**         |  **9.630 ms** | **3.7649 ms** | **0.2064 ms** |  **375.0000** |       **-** |   **6.93 MB** |
