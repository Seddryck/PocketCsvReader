using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Benchmark;

[MemoryDiagnoser]
public class JsonParserBenchmarks
{
    private const int ObjectCount = 50_000;
    private readonly JsonReader _pocketCsvReader = new JsonReaderBuilder()
        .WithProjection(projection => projection
            .Property("name")
            .Property("amount")
            .Property("count"))
        .Build();
    private byte[] _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _json = CreateJsonArray();

        var systemTextJson = SystemTextJson();
        var pocketCsvReader = PocketCsvReaderJson();
        if (systemTextJson.Objects != ObjectCount || systemTextJson != pocketCsvReader)
            throw new InvalidOperationException("The benchmark parsers returned different values.");
    }

    [Benchmark(Baseline = true)]
    public ParsedValues SystemTextJson()
    {
        var reader = new Utf8JsonReader(_json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new InvalidDataException("The benchmark payload must be a JSON array.");

        var nameLength = 0;
        var amount = 0m;
        var count = 0;
        var objects = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ReadProperty(ref reader, "name");
            nameLength += reader.GetString()!.Length;

            ReadProperty(ref reader, "amount");
            amount += reader.GetDecimal();

            ReadProperty(ref reader, "count");
            count += reader.GetInt32();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName || !reader.Read())
                    throw new InvalidDataException("The benchmark payload contains an invalid object.");
                reader.Skip();
            }

            objects++;
        }

        return new(objects, nameLength, amount, count);
    }

    [Benchmark]
    public ParsedValues PocketCsvReaderJson()
    {
        using var stream = new MemoryStream(_json, writable: false);
        using var reader = _pocketCsvReader.ToDataReader(stream);

        var nameLength = 0;
        var amount = 0m;
        var count = 0;
        var objects = 0;
        while (reader.Read())
        {
            nameLength += ((string)reader.GetValue(0)).Length;
            amount += reader.GetFieldValue<decimal>(1);
            count += reader.GetInt32(2);
            objects++;
        }

        return new(objects, nameLength, amount, count);
    }

    private static void ReadProperty(ref Utf8JsonReader reader, string expectedName)
    {
        if (!reader.Read()
            || reader.TokenType != JsonTokenType.PropertyName
            || !reader.ValueTextEquals(expectedName)
            || !reader.Read())
        {
            throw new InvalidDataException($"The benchmark payload is missing the '{expectedName}' property.");
        }
    }

    private static byte[] CreateJsonArray()
    {
        var random = new Random(42);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            for (var index = 0; index < ObjectCount; index++)
            {
                var attributeCount = 8 + index % 5;
                var date = new DateOnly(2000, 1, 1).AddDays(index % 9_000);
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index);

                writer.WriteStartObject();
                writer.WriteString("name", $"Object {index}");
                writer.WriteNumber("amount", decimal.Round((decimal)random.NextDouble() * 10_000m, 2));
                writer.WriteNumber("count", index % 1_000);
                writer.WriteString("date", date.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("timestamp", timestamp);
                writer.WriteString("category", $"Category {index % 25}");
                writer.WriteNumber("price", decimal.Round((decimal)random.NextDouble() * 250m, 2));
                writer.WriteNumber("quantity", random.Next(1, 100));

                if (attributeCount >= 9)
                    writer.WriteString("effectiveDate", date.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
                if (attributeCount >= 10)
                    writer.WriteString("updatedAt", timestamp.AddHours(1));
                if (attributeCount >= 11)
                    writer.WriteString("description", $"Description for object {index}");
                if (attributeCount >= 12)
                    writer.WriteNumber("balance", decimal.Round((decimal)random.NextDouble() * 50_000m, 2));

                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    public readonly record struct ParsedValues(int Objects, int NameLength, decimal Amount, int Count);
}
