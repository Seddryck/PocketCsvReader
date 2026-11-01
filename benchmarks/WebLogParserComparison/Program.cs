using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using PocketCsvReader.Benchmark;

if (args is ["--matrix"])
    WebLogParserComparison.RunMatrix();
else if (args is ["--validate"])
{
    foreach (var parser in Enum.GetValues<WebLogParserComparison.LogParser>())
        new WebLogParserComparison { Parser = parser }.Setup();
}
else
    BenchmarkSwitcher.FromTypes([typeof(WebLogParserComparison)]).Run(args,
        ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default.DontEnforcePowerPlan().AsDefault()));
