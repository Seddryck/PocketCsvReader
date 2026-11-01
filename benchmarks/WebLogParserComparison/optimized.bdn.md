```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method          | Parser          | Mean     | Error     | StdDev   | Gen0      | Gen1    | Allocated |
|---------------- |---------------- |---------:|----------:|---------:|----------:|--------:|----------:|
| **ReadThreeValues** | **CsvChars**        | **29.80 ms** | **13.795 ms** | **0.756 ms** | **1531.2500** | **31.2500** |  **27.82 MB** |
| **ReadThreeValues** | **CsvUtf8**         | **18.45 ms** |  **8.194 ms** | **0.449 ms** | **1531.2500** |       **-** |  **27.81 MB** |
| **ReadThreeValues** | **FixedWidth**      | **10.10 ms** | **13.389 ms** | **0.734 ms** |  **437.5000** |       **-** |   **7.98 MB** |
| **ReadThreeValues** | **Ndjson**          | **36.66 ms** | **14.783 ms** | **0.810 ms** |  **384.6154** |       **-** |   **8.22 MB** |
| **ReadThreeValues** | **Json**            | **35.53 ms** | **19.893 ms** | **1.090 ms** |  **400.0000** |       **-** |   **8.21 MB** |
| **ReadThreeValues** | **Logfmt**          | **17.21 ms** |  **9.789 ms** | **0.537 ms** |  **218.7500** |       **-** |   **4.38 MB** |
| **ReadThreeValues** | **Ltsv**            | **11.02 ms** | **16.972 ms** | **0.930 ms** |  **234.3750** |       **-** |   **4.36 MB** |
| **ReadThreeValues** | **CommonLog**       | **14.55 ms** | **13.844 ms** | **0.759 ms** |  **234.3750** |       **-** |   **4.29 MB** |
| **ReadThreeValues** | **CombinedLog**     | **17.17 ms** | **36.694 ms** | **2.011 ms** |  **234.3750** |       **-** |   **4.35 MB** |
| **ReadThreeValues** | **W3c**             | **12.99 ms** | **17.177 ms** | **0.942 ms** |  **375.0000** |       **-** |   **6.93 MB** |
| **ReadThreeValues** | **JsonProjected**   | **22.70 ms** |  **6.525 ms** | **0.358 ms** |  **218.7500** |       **-** |    **4.4 MB** |
| **ReadThreeValues** | **NdjsonProjected** | **29.97 ms** |  **6.121 ms** | **0.336 ms** |  **187.5000** |       **-** |    **4.4 MB** |
