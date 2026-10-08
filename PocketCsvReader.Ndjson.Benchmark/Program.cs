using BenchmarkDotNet.Running;

namespace PocketCsvReader.Ndjson.Benchmark;

internal static class Program
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
