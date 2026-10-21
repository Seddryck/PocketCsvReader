using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace PocketCsvReader.Benchmark;
public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0)
            BenchmarkSwitcher.FromTypes([typeof(CustomParsing), typeof(Utf8Input)]).Run(args,
                ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default.DontEnforcePowerPlan().AsDefault()));
        else
            BenchmarkRunner.Run<ToDataReader>();
    }
}
