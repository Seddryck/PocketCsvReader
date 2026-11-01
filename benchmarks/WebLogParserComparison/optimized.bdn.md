```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method          | Parser          | Mean      | Error      | StdDev    | Gen0      | Gen1    | Allocated |
|---------------- |---------------- |----------:|-----------:|----------:|----------:|--------:|----------:|
| **ReadThreeValues** | **CsvChars**        | **26.147 ms** |  **2.0706 ms** | **0.1135 ms** | **1531.2500** | **31.2500** |  **27.82 MB** |
| **ReadThreeValues** | **CsvUtf8**         | **17.673 ms** |  **0.2347 ms** | **0.0129 ms** | **1531.2500** |       **-** |  **27.81 MB** |
| **ReadThreeValues** | **FixedWidth**      |  **7.953 ms** |  **0.6492 ms** | **0.0356 ms** |  **437.5000** |       **-** |   **7.98 MB** |
| **ReadThreeValues** | **Ndjson**          | **28.964 ms** |  **1.4080 ms** | **0.0772 ms** |  **218.7500** |       **-** |    **4.4 MB** |
| **ReadThreeValues** | **Json**            | **28.768 ms** |  **2.8105 ms** | **0.1541 ms** |  **218.7500** |       **-** |    **4.4 MB** |
| **ReadThreeValues** | **Logfmt**          | **16.466 ms** |  **1.3656 ms** | **0.0749 ms** |  **218.7500** |       **-** |   **4.38 MB** |
| **ReadThreeValues** | **Ltsv**            | **12.043 ms** |  **0.9833 ms** | **0.0539 ms** |  **234.3750** |       **-** |   **4.36 MB** |
| **ReadThreeValues** | **CommonLog**       | **13.683 ms** |  **7.8280 ms** | **0.4291 ms** |  **234.3750** |       **-** |   **4.29 MB** |
| **ReadThreeValues** | **CombinedLog**     | **12.690 ms** |  **2.1963 ms** | **0.1204 ms** |  **234.3750** |       **-** |   **4.35 MB** |
| **ReadThreeValues** | **W3c**             | **12.653 ms** |  **1.4908 ms** | **0.0817 ms** |  **375.0000** |       **-** |   **6.93 MB** |
| **ReadThreeValues** | **JsonProjected**   | **24.886 ms** |  **5.1742 ms** | **0.2836 ms** |  **218.7500** |       **-** |    **4.4 MB** |
| **ReadThreeValues** | **NdjsonProjected** | **34.231 ms** | **27.2324 ms** | **1.4927 ms** |  **200.0000** |       **-** |    **4.4 MB** |
