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
        if (args is ["--lazy-json-matrix"])
        {
            LazyJsonDecodingComparison.RunMatrix();
            return;
        }
        if (args is ["--json-allocation-matrix"])
        {
            JsonAllocationComparison.RunMatrix();
            return;
        }
        if (args is ["--keyvalue-buffering-matrix"])
        {
            KeyValueBuffering.RunMatrix();
            return;
        }
        if (args is ["--position-tracking-matrix"])
        {
            PositionTracking.RunMatrix();
            return;
        }
        if (args.Length > 0)
            BenchmarkSwitcher.FromTypes([typeof(LazyJsonDecodingComparison), typeof(JsonAllocationComparison), typeof(CustomParsing), typeof(ChrononuensisParsing), typeof(Utf8Input), typeof(PositionTracking), typeof(CharacterRecordParsing), typeof(CommonLogComparison), typeof(WebLogBuffering), typeof(FixedWidthBuffering)]).Run(args,
                ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default.DontEnforcePowerPlan().AsDefault()));
        else
            BenchmarkRunner.Run<ToDataReader>();
    }
}
