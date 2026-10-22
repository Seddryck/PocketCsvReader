```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.401
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

PowerPlanMode=00000000-0000-0000-0000-000000000000  Toolchain=InProcessEmitToolchain  IterationCount=10
IterationTime=500ms  LaunchCount=1  WarmupCount=5

```
| Method | Parser       | Tracking | Scenario           | Mean     | Error     | StdDev    | Gen0     | Gen1   | Allocated |
|------- |------------- |--------- |------------------- |---------:|----------:|----------:|---------:|-------:|----------:|
| **Read**   | **DecodedChars** | **False**    | **WideUnicodeSkipped** | **2.155 ms** | **0.2963 ms** | **0.1960 ms** | **150.0000** |      **-** |   **2.78 MB** |
| **Read**   | **DecodedChars** | **True**     | **WideUnicodeSkipped** | **2.455 ms** | **0.0361 ms** | **0.0238 ms** | **153.8462** | **4.8077** |   **2.78 MB** |
| **Read**   | **Utf8Bytes**    | **False**    | **WideUnicodeSkipped** | **1.921 ms** | **0.0266 ms** | **0.0139 ms** | **154.4118** | **3.6765** |   **2.77 MB** |
| **Read**   | **Utf8Bytes**    | **True**     | **WideUnicodeSkipped** | **1.970 ms** | **0.0205 ms** | **0.0122 ms** | **152.3438** | **3.9063** |   **2.77 MB** |
