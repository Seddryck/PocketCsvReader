using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;

namespace PocketCsvReader.Benchmark;
public class Program
{
    public static void Main()
    {
        var summary = BenchmarkRunner.Run<ToDataReader>();
    }
}
