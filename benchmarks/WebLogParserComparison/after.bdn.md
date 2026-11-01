```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method          | Parser          | Mean      | Error     | StdDev    | Gen0     | Allocated |
|---------------- |---------------- |----------:|----------:|----------:|---------:|----------:|
| **ReadThreeValues** | **CsvChars**        | **25.998 ms** | **1.7708 ms** | **0.0971 ms** | **218.7500** |   **4.17 MB** |
| **ReadThreeValues** | **CsvUtf8**         | **16.352 ms** | **0.8550 ms** | **0.0469 ms** | **218.7500** |   **4.16 MB** |
| **ReadThreeValues** | **FixedWidth**      |  **7.883 ms** | **1.1195 ms** | **0.0614 ms** | **218.7500** |   **4.17 MB** |
| **ReadThreeValues** | **Ndjson**          | **30.113 ms** | **0.9909 ms** | **0.0543 ms** | **218.7500** |    **4.4 MB** |
| **ReadThreeValues** | **Json**            | **30.117 ms** | **1.0765 ms** | **0.0590 ms** | **218.7500** |    **4.4 MB** |
| **ReadThreeValues** | **Logfmt**          | **17.316 ms** | **2.7417 ms** | **0.1503 ms** | **218.7500** |   **4.38 MB** |
| **ReadThreeValues** | **Ltsv**            | **10.355 ms** | **0.1916 ms** | **0.0105 ms** | **234.3750** |   **4.36 MB** |
| **ReadThreeValues** | **CommonLog**       | **11.622 ms** | **4.7187 ms** | **0.2586 ms** | **234.3750** |   **4.29 MB** |
| **ReadThreeValues** | **CombinedLog**     | **11.445 ms** | **1.0889 ms** | **0.0597 ms** | **234.3750** |   **4.35 MB** |
| **ReadThreeValues** | **W3c**             |  **9.812 ms** | **1.2177 ms** | **0.0667 ms** | **375.0000** |   **6.93 MB** |
| **ReadThreeValues** | **JsonProjected**   | **19.840 ms** | **0.3401 ms** | **0.0186 ms** | **218.7500** |    **4.4 MB** |
| **ReadThreeValues** | **NdjsonProjected** | **26.962 ms** | **5.2852 ms** | **0.2897 ms** | **218.7500** |    **4.4 MB** |
