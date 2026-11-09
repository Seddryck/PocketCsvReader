using System.Globalization;
using Apache.Arrow;
using Apache.Arrow.Types;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal static class ClickHouseMetadata
{
    public static Schema ParseSchema(ReadOnlySpan<char> json, JsonFieldBuffer scratch, CancellationToken token)
    {
        var entries = JsonRecordParser.Enumerate(json);
        var fields = new List<Field>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (entries.MoveNext())
        {
            token.ThrowIfCancellationRequested();
            var entry = entries.Value;
            var name = JsonValues.Text(JsonValues.Property(entry, "name", scratch), scratch).ToString();
            if (name.Length == 0 || !names.Add(name))
                throw new InvalidDataException($"Empty or duplicate ClickHouse column name '{name}'.");

            var declaration = JsonValues.Text(JsonValues.Property(entry, "type", scratch), scratch);
            if (declaration.IsEmpty)
                throw new InvalidDataException($"Missing ClickHouse type for column '{name}'.");
            fields.Add(ParseField(name, declaration));
        }
        return new Schema(fields, null);
    }

    private static Field ParseField(string name, ReadOnlySpan<char> declaration)
    {
        var type = ParseType(declaration.Trim(), allowArray: true, depth: 0, out var nullable);
        if (type is null)
            throw new NotSupportedException($"ClickHouse type '{declaration.ToString()}' is unsupported for column '{name}'.");
        return new Field(name, type, nullable);
    }

    private static IArrowType? ParseType(ReadOnlySpan<char> type, bool allowArray, int depth, out bool nullable)
    {
        nullable = false;
        if (depth == 16)
            return null;
        if (TryUnwrap(type, "Nullable", out var inner))
        {
            var result = ParseType(inner, allowArray, depth + 1, out _);
            nullable = true;
            return result;
        }
        if (TryUnwrap(type, "LowCardinality", out inner))
            return ParseType(inner, allowArray, depth + 1, out nullable);
        if (allowArray && TryUnwrap(type, "Array", out inner))
        {
            var item = ParseType(inner, allowArray: false, depth + 1, out var itemNullable);
            return item is null ? null : new ListType(new Field("item", item, itemNullable));
        }

        return type switch
        {
            "Bool" => BooleanType.Default,
            "Int8" => Int8Type.Default,
            "UInt8" => UInt8Type.Default,
            "Int16" => Int16Type.Default,
            "UInt16" => UInt16Type.Default,
            "Int32" => Int32Type.Default,
            "UInt32" => UInt32Type.Default,
            "Int64" => Int64Type.Default,
            "UInt64" => UInt64Type.Default,
            "Float32" => FloatType.Default,
            "Float64" => DoubleType.Default,
            "String" or "UUID" or "IPv4" or "IPv6" => StringType.Default,
            "Date" or "Date32" => Date32Type.Default,
            _ => Parameterized(type),
        };
    }

    private static IArrowType? Parameterized(ReadOnlySpan<char> type)
    {
        if (TryUnwrap(type, "FixedString", out var inner))
            return TryCount(inner, out var width) && width > 0 ? new FixedSizeBinaryType(width) : null;
        if (TryUnwrap(type, "Decimal", out inner))
        {
            var comma = inner.IndexOf(',');
            if (comma < 0 || !TryCount(inner[..comma], out var precision))
                return null;
            return Decimal(precision, inner[(comma + 1)..]);
        }

        if (TryUnwrap(type, "Decimal32", out inner))
            return Decimal(9, inner);
        if (TryUnwrap(type, "Decimal64", out inner))
            return Decimal(18, inner);
        if (TryUnwrap(type, "Decimal128", out inner))
            return Decimal(38, inner);
        if (TryUnwrap(type, "Decimal256", out inner))
            return Decimal(76, inner);
        return null;
    }

    private static IArrowType? Decimal(int precision, ReadOnlySpan<char> scaleText)
    {
        if (precision is < 1 or > 76 || !TryCount(scaleText, out var scale) || scale > precision)
            return null;
        return precision <= 38 ? new Decimal128Type(precision, scale) : new Decimal256Type(precision, scale);
    }

    private static bool TryCount(ReadOnlySpan<char> value, out int count)
        => int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count);

    private static bool TryUnwrap(ReadOnlySpan<char> declaration, string name, out ReadOnlySpan<char> inner)
    {
        inner = default;
        if (!declaration.StartsWith(name, StringComparison.Ordinal))
            return false;
        var arguments = declaration[name.Length..].Trim();
        if (arguments.Length < 2 || arguments[0] != '(' || arguments[^1] != ')')
            return false;
        inner = arguments[1..^1].Trim();
        return true;
    }
}
