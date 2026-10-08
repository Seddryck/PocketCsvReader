using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Benchmark;

[MemoryDiagnoser]
public class StableObjectShapeBenchmarks
{
    private const int ObjectCount = 50_000;
    private readonly NdjsonReader _ordinaryReader = new NdjsonReaderBuilder()
        .WithDialect(dialect => dialect.WithLineTerminator("\n"))
        .Build();
    private readonly NdjsonReader _stableShapeReader = new NdjsonReaderBuilder()
        .WithDialect(dialect => dialect.WithLineTerminator("\n"))
        .WithStableObjectShape()
        .Build();
    private byte[] _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = CreatePayload();
        var ordinary = Read(_ordinaryReader);
        var stable = Read(_stableShapeReader);
        if (ordinary != stable || ordinary.Objects != ObjectCount)
            throw new InvalidOperationException("The benchmark readers returned different values.");
    }

    [Benchmark(Baseline = true)]
    public ParsedValues OrdinaryShape()
        => Read(_ordinaryReader);

    [Benchmark]
    public ParsedValues StableShape()
        => Read(_stableShapeReader);

    private ParsedValues Read(NdjsonReader ndjson)
    {
        using var stream = new MemoryStream(_payload, writable: false);
        using var reader = ndjson.ToDataReader(stream);

        var objects = 0;
        var idTotal = 0L;
        var nameLength = 0;
        var amountTotal = 0m;
        var countTotal = 0;
        while (reader.Read())
        {
            idTotal += reader.GetInt32(0);
            nameLength += reader.GetString(1).Length;
            amountTotal += reader.GetDecimal(3);
            countTotal += reader.GetInt32(8);
            objects++;
        }

        return new(objects, idTotal, nameLength, amountTotal, countTotal);
    }

    private static byte[] CreatePayload()
    {
        var builder = new StringBuilder(ObjectCount * 300);
        for (var index = 0; index < ObjectCount; index++)
        {
            var amount = (index % 10_000) / 100m;
            var ratio = (index % 1_000) / 10m;
            builder.Append("{\"id\":").Append(index)
                .Append(",\"name\":\"Object ").Append(index)
                .Append("\",\"active\":").Append(index % 2 == 0 ? "true" : "false")
                .Append(",\"amount\":").Append(amount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"timestamp\":\"2026-10-08T07:00:00Z\"")
                .Append(",\"category\":\"Category ").Append(index % 25)
                .Append("\",\"tags\":[\"alpha\",\"beta\"]")
                .Append(",\"metadata\":{\"source\":\"benchmark\",\"rank\":").Append(index % 10).Append('}')
                .Append(",\"count\":").Append(index % 1_000)
                .Append(",\"ratio\":").Append(ratio.ToString(CultureInfo.InvariantCulture))
                .Append(",\"note\":\"escaped \\\"value\\\"\"")
                .Append(",\"optional\":null}\n");
        }
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public readonly record struct ParsedValues(
        int Objects,
        long IdTotal,
        int NameLength,
        decimal AmountTotal,
        int CountTotal);
}
