using Apache.Arrow;
using Apache.Arrow.Types;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal static class IntegrationSchema
{
    public static Schema Parse(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
    {
        var fields = new List<Field>();
        var items = JsonRecordParser.Enumerate(JsonValues.Property(json, "fields", scratch));
        while (items.MoveNext())
            fields.Add(ParseField(items.Value, scratch));
        return new Schema(fields, Metadata(json, scratch));
    }

    private static Field ParseField(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
    {
        var name = JsonValues.Text(JsonValues.Property(json, "name", scratch), scratch).ToString();
        var nullable = JsonValues.Boolean(JsonValues.Property(json, "nullable", scratch));
        if (!JsonValues.Property(json, "dictionary", scratch, required: false).IsEmpty)
            throw new NotSupportedException($"Arrow dictionary encoding is unsupported for field '{name}'.");

        var type = ParseType(JsonValues.Property(json, "type", scratch), name, scratch);
        var children = JsonValues.Property(json, "children", scratch, required: false);
        if (!children.IsEmpty && JsonRecordParser.Enumerate(children).MoveNext())
            throw new InvalidDataException($"Unexpected children for Arrow field '{name}'.");
        return new Field(name, type, nullable, Metadata(json, scratch));
    }

    private static Dictionary<string, string>? Metadata(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
    {
        var value = JsonValues.Property(json, "metadata", scratch, required: false);
        if (value.IsEmpty)
            return null;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = JsonRecordParser.Enumerate(value);
        while (entries.MoveNext())
        {
            var key = JsonValues.Text(JsonValues.Property(entries.Value, "key", scratch), scratch).ToString();
            var text = JsonValues.Text(JsonValues.Property(entries.Value, "value", scratch), scratch).ToString();
            if (!metadata.TryAdd(key, text))
                throw new InvalidDataException($"Duplicate Arrow metadata key '{key}'.");
        }
        return metadata;
    }

    private static IArrowType ParseType(ReadOnlySpan<char> json, string field, JsonFieldBuffer scratch)
    {
        var name = JsonValues.Text(JsonValues.Property(json, "name", scratch), scratch);
        return name switch
        {
            "null" => NullType.Default,
            "bool" => BooleanType.Default,
            "int" => IntegerType(json, scratch),
            "floatingpoint" => JsonValues.Text(JsonValues.Property(json, "precision", scratch), scratch) switch
            {
                "HALF" => HalfFloatType.Default,
                "SINGLE" => FloatType.Default,
                "DOUBLE" => DoubleType.Default,
                _ => throw new InvalidDataException($"Invalid floating-point precision for Arrow field '{field}'."),
            },
            "utf8" => StringType.Default,
            "binary" => BinaryType.Default,
            "fixedsizebinary" => new FixedSizeBinaryType(Positive(json, "byteWidth", scratch)),
            "date" => JsonValues.Text(JsonValues.Property(json, "unit", scratch), scratch) switch
            {
                "DAY" => Date32Type.Default,
                "MILLISECOND" => Date64Type.Default,
                _ => throw new InvalidDataException($"Invalid date unit for Arrow field '{field}'."),
            },
            "time" => (Positive(json, "bitWidth", scratch), Unit(json, scratch)) switch
            {
                (32, TimeUnit.Second or TimeUnit.Millisecond) pair => new Time32Type(pair.Item2),
                (64, TimeUnit.Microsecond or TimeUnit.Nanosecond) pair => new Time64Type(pair.Item2),
                _ => throw new InvalidDataException($"Invalid time width or unit for Arrow field '{field}'."),
            },
            "timestamp" => new TimestampType(Unit(json, scratch), Timezone(json, scratch)),
            "duration" => DurationType.FromTimeUnit(Unit(json, scratch)),
            "decimal" => DecimalType(json, scratch),
            _ => throw new NotSupportedException($"Arrow type '{name.ToString()}' is unsupported for field '{field}'."),
        };
    }

    private static IArrowType IntegerType(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
        => (Positive(json, "bitWidth", scratch), JsonValues.Boolean(JsonValues.Property(json, "isSigned", scratch))) switch
        {
            (8, true) => Int8Type.Default,
            (8, false) => UInt8Type.Default,
            (16, true) => Int16Type.Default,
            (16, false) => UInt16Type.Default,
            (32, true) => Int32Type.Default,
            (32, false) => UInt32Type.Default,
            (64, true) => Int64Type.Default,
            (64, false) => UInt64Type.Default,
            _ => throw new InvalidDataException("Invalid Arrow integer bit width."),
        };

    private static IArrowType DecimalType(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
    {
        var width = JsonValues.Property(json, "bitWidth", scratch, required: false);
        var precision = Positive(json, "precision", scratch);
        var scale = JsonValues.Integer(JsonValues.Property(json, "scale", scratch));
        return (width.IsEmpty ? 128 : JsonValues.Count(width)) switch
        {
            128 when precision <= 38 => new Decimal128Type(precision, scale),
            256 when precision <= 76 => new Decimal256Type(precision, scale),
            128 or 256 => throw new InvalidDataException("Invalid Arrow decimal precision."),
            var bits => throw new NotSupportedException($"Arrow decimal bit width {bits} is unsupported."),
        };
    }

    private static int Positive(ReadOnlySpan<char> json, string property, JsonFieldBuffer scratch)
    {
        var value = JsonValues.Count(JsonValues.Property(json, property, scratch));
        return value > 0 ? value : throw new InvalidDataException($"Arrow '{property}' must be positive.");
    }

    private static string? Timezone(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
    {
        var timezone = JsonValues.Property(json, "timezone", scratch, required: false);
        return timezone.IsEmpty ? null : JsonValues.Text(timezone, scratch).ToString();
    }

    private static TimeUnit Unit(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
        => JsonValues.Text(JsonValues.Property(json, "unit", scratch), scratch) switch
        {
            "SECOND" => TimeUnit.Second,
            "MILLISECOND" => TimeUnit.Millisecond,
            "MICROSECOND" => TimeUnit.Microsecond,
            "NANOSECOND" => TimeUnit.Nanosecond,
            _ => throw new InvalidDataException("Invalid Arrow time unit."),
        };
}
