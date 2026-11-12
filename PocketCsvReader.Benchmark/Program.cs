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
    private static readonly Dictionary<string, Action> MatrixCommands = new(StringComparer.Ordinal)
    {
        ["--arrow-batch-timing"] = () => ArrowBatchTiming.RunMatrixAsync().GetAwaiter().GetResult(),
        ["--reader-filtering-matrix"] = ReaderFiltering.RunMatrix,
        ["--lazy-json-matrix"] = LazyJsonDecodingComparison.RunMatrix,
        ["--json-allocation-matrix"] = JsonAllocationComparison.RunMatrix,
        ["--keyvalue-buffering-matrix"] = KeyValueBuffering.RunMatrix,
        ["--labeled-layout-matrix"] = LabeledLayoutBenchmarks.RunMatrix,
        ["--position-tracking-matrix"] = PositionTracking.RunMatrix,
        ["--sparse-index"] = SparseIndexBenchmark.Run,
        ["--zone-map"] = ZoneMapBenchmark.Run,
        ["--csv-index-comparison"] = CsvIndexComparison.Run,
    };

    public static void Main(string[] args)
    {
        if (args is [string command] && MatrixCommands.TryGetValue(command, out var run))
        {
            run();
            return;
        }

        if (args.Length > 0)
        {
            BenchmarkSwitcher.FromTypes([typeof(LazyJsonDecodingComparison), typeof(JsonAllocationComparison), typeof(CustomParsing), typeof(ChrononuensisParsing), typeof(Utf8Input), typeof(PositionTracking), typeof(CharacterRecordParsing), typeof(CommonLogComparison), typeof(WebLogBuffering), typeof(FixedWidthBuffering)]).Run(args,
                ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default.DontEnforcePowerPlan().AsDefault()));
        }
        else
        {
            BenchmarkRunner.Run<ToDataReader>();
        }
    }
}
