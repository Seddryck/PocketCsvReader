using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Benchmark;

[MemoryDiagnoser]
public class JsonParserBenchmarks
{
    private const int ObjectCount = 50_000;
    private readonly JsonReader _orderedPocketCsvReader = new JsonReaderBuilder()
        .WithOrderedProjection(projection => projection
            .Property("name")
            .Property("amount")
            .Property("count"))
        .Build();
    private readonly JsonReader _unorderedPocketCsvReader = new JsonReaderBuilder()
        .WithProjection(projection => projection
            .Property("name")
            .Property("amount")
            .Property("count"))
        .Build();
    private byte[] _consistentOrderJson = null!;
    private byte[] _randomOrderJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        _consistentOrderJson = CreateJsonArray(randomizePropertyOrder: false);
        _randomOrderJson = CreateJsonArray(randomizePropertyOrder: true);

        var expected = SystemTextJsonConsistentOrder();
        if (expected.Objects != ObjectCount
            || expected != PocketCsvReaderJsonConsistentOrder()
            || expected != SystemTextJsonRandomOrder()
            || expected != PocketCsvReaderJsonRandomOrder())
        {
            throw new InvalidOperationException("The benchmark parsers returned different values.");
        }
    }

    [Benchmark(Baseline = true)]
    public ParsedValues SystemTextJsonConsistentOrder()
    {
        var reader = new Utf8JsonReader(_consistentOrderJson);
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

            SkipRemainingProperties(ref reader);
            objects++;
        }

        return new(objects, nameLength, amount, count);
    }

    [Benchmark]
    public ParsedValues PocketCsvReaderJsonConsistentOrder()
        => ReadWithPocketCsvReader(_consistentOrderJson, _orderedPocketCsvReader);

    [Benchmark]
    public ParsedValues SystemTextJsonRandomOrder()
    {
        var reader = new Utf8JsonReader(_randomOrderJson);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new InvalidDataException("The benchmark payload must be a JSON array.");

        var nameLength = 0;
        var amount = 0m;
        var count = 0;
        var objects = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new InvalidDataException("The benchmark payload contains an invalid object.");

            var foundName = false;
            var foundAmount = false;
            var foundCount = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new InvalidDataException("The benchmark payload contains an invalid object.");

                if (!foundName && reader.ValueTextEquals("name"))
                {
                    ReadPropertyValue(ref reader, "name");
                    nameLength += reader.GetString()!.Length;
                    foundName = true;
                }
                else if (!foundAmount && reader.ValueTextEquals("amount"))
                {
                    ReadPropertyValue(ref reader, "amount");
                    amount += reader.GetDecimal();
                    foundAmount = true;
                }
                else if (!foundCount && reader.ValueTextEquals("count"))
                {
                    ReadPropertyValue(ref reader, "count");
                    count += reader.GetInt32();
                    foundCount = true;
                }
                else
                {
                    ReadPropertyValue(ref reader, "a skipped property");
                    reader.Skip();
                }
            }

            if (!foundName || !foundAmount || !foundCount)
                throw new InvalidDataException("The benchmark payload is missing a projected property.");

            objects++;
        }

        return new(objects, nameLength, amount, count);
    }

    [Benchmark]
    public ParsedValues PocketCsvReaderJsonRandomOrder()
        => ReadWithPocketCsvReader(_randomOrderJson, _unorderedPocketCsvReader);

    private static ParsedValues ReadWithPocketCsvReader(byte[] json, JsonReader jsonReader)
    {
        using var stream = new MemoryStream(json, writable: false);
        using var reader = jsonReader.ToDataReader(stream);

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
            || !reader.ValueTextEquals(expectedName))
        {
            throw new InvalidDataException($"The benchmark payload is missing the '{expectedName}' property.");
        }

        ReadPropertyValue(ref reader, expectedName);
    }

    private static void ReadPropertyValue(ref Utf8JsonReader reader, string propertyName)
    {
        if (!reader.Read())
            throw new InvalidDataException($"The benchmark payload is missing the value of '{propertyName}'.");
    }

    private static void SkipRemainingProperties(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new InvalidDataException("The benchmark payload contains an invalid object.");

            ReadPropertyValue(ref reader, "a skipped property");
            reader.Skip();
        }
    }

    private static byte[] CreateJsonArray(bool randomizePropertyOrder)
    {
        var valueRandom = new Random(42);
        var orderRandom = new Random(43);
        var propertyOrder = new int[12];
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            for (var index = 0; index < ObjectCount; index++)
            {
                var attributeCount = 8 + index % 5;
                var date = new DateOnly(2000, 1, 1).AddDays(index % 9_000);
                var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index);
                var amount = decimal.Round((decimal)valueRandom.NextDouble() * 10_000m, 2);
                var price = decimal.Round((decimal)valueRandom.NextDouble() * 250m, 2);
                var quantity = valueRandom.Next(1, 100);
                var balance = attributeCount >= 12
                    ? decimal.Round((decimal)valueRandom.NextDouble() * 50_000m, 2)
                    : 0m;

                for (var propertyIndex = 0; propertyIndex < attributeCount; propertyIndex++)
                    propertyOrder[propertyIndex] = propertyIndex;

                if (randomizePropertyOrder)
                    Shuffle(propertyOrder.AsSpan(0, attributeCount), orderRandom);

                writer.WriteStartObject();
                for (var propertyIndex = 0; propertyIndex < attributeCount; propertyIndex++)
                {
                    WriteProperty(
                        writer,
                        propertyOrder[propertyIndex],
                        index,
                        date,
                        timestamp,
                        amount,
                        price,
                        quantity,
                        balance);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    private static void Shuffle(Span<int> values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }

    private static void WriteProperty(
        Utf8JsonWriter writer,
        int propertyIndex,
        int objectIndex,
        DateOnly date,
        DateTime timestamp,
        decimal amount,
        decimal price,
        int quantity,
        decimal balance)
    {
        switch (propertyIndex)
        {
            case 0:
                writer.WriteString("name", $"Object {objectIndex}");
                break;
            case 1:
                writer.WriteNumber("amount", amount);
                break;
            case 2:
                writer.WriteNumber("count", objectIndex % 1_000);
                break;
            case 3:
                writer.WriteString("date", date.ToString("O", CultureInfo.InvariantCulture));
                break;
            case 4:
                writer.WriteString("timestamp", timestamp);
                break;
            case 5:
                writer.WriteString("category", $"Category {objectIndex % 25}");
                break;
            case 6:
                writer.WriteNumber("price", price);
                break;
            case 7:
                writer.WriteNumber("quantity", quantity);
                break;
            case 8:
                writer.WriteString("effectiveDate", date.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
                break;
            case 9:
                writer.WriteString("updatedAt", timestamp.AddHours(1));
                break;
            case 10:
                writer.WriteString("description", $"Description for object {objectIndex}");
                break;
            case 11:
                writer.WriteNumber("balance", balance);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(propertyIndex));
        }
    }

    public readonly record struct ParsedValues(int Objects, int NameLength, decimal Amount, int Count);
}
