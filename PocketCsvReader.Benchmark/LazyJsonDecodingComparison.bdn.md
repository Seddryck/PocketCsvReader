```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]             : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun-.NET 10.0 : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun-.NET 10.0  PowerPlanMode=00000000-0000-0000-0000-000000000000  Runtime=.NET 10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method | Ndjson | AccessTimestamp | Mean     | Error    | StdDev   | Gen0     | Allocated |
|------- |------- |---------------- |---------:|---------:|---------:|---------:|----------:|
| **Read**   | **False**  | **False**           | **34.90 ms** | **46.03 ms** | **2.523 ms** | **214.2857** |    **4.4 MB** |
| **Read**   | **False**  | **True**            | **41.46 ms** | **20.02 ms** | **1.098 ms** | **384.6154** |   **8.21 MB** |
| **Read**   | **True**   | **False**           | **32.98 ms** | **18.29 ms** | **1.002 ms** | **230.7692** |    **4.4 MB** |
| **Read**   | **True**   | **True**            | **35.14 ms** | **11.46 ms** | **0.628 ms** | **400.0000** |   **8.22 MB** |
