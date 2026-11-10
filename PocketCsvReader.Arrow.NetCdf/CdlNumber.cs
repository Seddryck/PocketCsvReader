using System.Globalization;
using System.Numerics;
using Apache.Arrow.Types;

namespace PocketCsvReader.Arrow.NetCdf;

internal static class CdlNumber
{
    public static IArrowType? GetType(ReadOnlySpan<char> name) => name switch
    {
        "byte" => Int8Type.Default,
        "short" => Int16Type.Default,
        "int" or "long" => Int32Type.Default,
        "int64" => Int64Type.Default,
        "ubyte" => UInt8Type.Default,
        "ushort" => UInt16Type.Default,
        "uint" => UInt32Type.Default,
        "uint64" => UInt64Type.Default,
        "float" or "real" => FloatType.Default,
        "double" => DoubleType.Default,
        _ => null,
    };

    public static ulong Parse(CdlToken token, IArrowType type, string? variable = null)
    {
        var text = token.Text.Span;
        if (token.Kind == 'w')
        {
            var (length, _) = SplitSuffix(text);
            var number = text[..length];
            ulong? value = type.TypeId switch
            {
                ArrowTypeId.Int8 => Integer<sbyte>(number),
                ArrowTypeId.Int16 => Integer<short>(number),
                ArrowTypeId.Int32 => Integer<int>(number),
                ArrowTypeId.Int64 => Integer<long>(number),
                ArrowTypeId.UInt8 => Integer<byte>(number),
                ArrowTypeId.UInt16 => Integer<ushort>(number),
                ArrowTypeId.UInt32 => Integer<uint>(number),
                ArrowTypeId.UInt64 => Integer<ulong>(number),
                ArrowTypeId.Float => Float(number),
                ArrowTypeId.Double => Double(number),
                _ => throw new InvalidOperationException($"Unexpected Arrow type '{type.Name}'."),
            };
            if (value is { } bits)
                return bits;
        }

        var context = variable is null ? string.Empty : $" for variable '{variable}'";
        throw token.Error($"Invalid {type.Name} constant '{text.ToString()}'{context}");
    }

    public static IArrowType InferType(ReadOnlySpan<char> text)
    {
        var (length, type) = SplitSuffix(text);
        if (type is not null)
            return type;
        var number = text[..length];
        if (!IsHex(number) && (number.IndexOfAny('.', 'e', 'E') >= 0 || IsSpecial(number)))
            return DoubleType.Default;
        return Int32Type.Default;
    }

    public static bool Equal(IArrowType type, ulong left, ulong right) => type.TypeId switch
    {
        ArrowTypeId.Float => BitConverter.UInt32BitsToSingle((uint)left).Equals(BitConverter.UInt32BitsToSingle((uint)right)),
        ArrowTypeId.Double => BitConverter.UInt64BitsToDouble(left).Equals(BitConverter.UInt64BitsToDouble(right)),
        _ => left == right,
    };

    private static (int Length, IArrowType? Type) SplitSuffix(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || char.IsAsciiDigit(text[^1]) || IsSpecial(text))
            return (text.Length, null);

        if (text.EndsWith("ull", StringComparison.OrdinalIgnoreCase))
            return (text.Length - 3, UInt64Type.Default);
        if (text.EndsWith("ub", StringComparison.OrdinalIgnoreCase))
            return (text.Length - 2, UInt8Type.Default);
        if (text.EndsWith("us", StringComparison.OrdinalIgnoreCase))
            return (text.Length - 2, UInt16Type.Default);
        if (text.EndsWith("ll", StringComparison.OrdinalIgnoreCase))
            return (text.Length - 2, Int64Type.Default);
        var type = SingleSuffixType(char.ToLowerInvariant(text[^1]), IsHex(text));
        return (type is null ? text.Length : text.Length - 1, type);
    }

    // In a hexadecimal constant a trailing b, d or f is a digit.
    private static IArrowType? SingleSuffixType(char suffix, bool hex) => suffix switch
    {
        'u' => UInt32Type.Default,
        's' => Int16Type.Default,
        'l' => Int32Type.Default,
        'b' when !hex => Int8Type.Default,
        'f' when !hex => FloatType.Default,
        'd' when !hex => DoubleType.Default,
        _ => null,
    };

    private static ReadOnlySpan<char> WithoutSign(ReadOnlySpan<char> text)
        => !text.IsEmpty && text[0] is '+' or '-' ? text[1..] : text;

    private static bool IsHex(ReadOnlySpan<char> text)
        => WithoutSign(text).StartsWith("0x", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpecial(ReadOnlySpan<char> text)
        => WithoutSign(text).Equals("nan", StringComparison.OrdinalIgnoreCase)
            || WithoutSign(text).Equals("infinity", StringComparison.OrdinalIgnoreCase);

    private static ulong? Integer<T>(ReadOnlySpan<char> text)
        where T : unmanaged, IBinaryInteger<T>, IMinMaxValue<T>
    {
        var digits = WithoutSign(text);
        if (digits.Length < 2 || digits[0] != '0')
        {
            return T.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                ? ulong.CreateTruncating(parsed)
                : null;
        }

        if (!TryBasedInteger(text, digits, out var value) || value < Int128.CreateChecked(T.MinValue)
            || value > Int128.CreateChecked(T.MaxValue))
            return null;
        return ulong.CreateTruncating(value);
    }

    private static bool TryBasedInteger(ReadOnlySpan<char> text, ReadOnlySpan<char> digits, out Int128 value)
    {
        value = 0;
        var radix = digits[1] is 'x' or 'X' ? 16 : 8;
        if (radix == 16)
            digits = digits[2..];
        if (digits.IsEmpty)
            return false;

        foreach (var character in digits)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => radix,
            };
            if (digit >= radix || value > (ulong.MaxValue - (uint)digit) / (uint)radix)
                return false;
            value = (value * radix) + digit;
        }

        if (text[0] == '-')
            value = -value;
        return true;
    }

    private static ulong? Float(ReadOnlySpan<char> text)
    {
        if (IsBasedInteger(text))
            return TryBasedInteger(text, WithoutSign(text), out var integer) ? BitConverter.SingleToUInt32Bits((float)integer) : null;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && (!float.IsInfinity(value) || IsSpecial(text)) ? BitConverter.SingleToUInt32Bits(value) : null;
    }

    private static ulong? Double(ReadOnlySpan<char> text)
    {
        if (IsBasedInteger(text))
            return TryBasedInteger(text, WithoutSign(text), out var integer) ? BitConverter.DoubleToUInt64Bits((double)integer) : null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && (!double.IsInfinity(value) || IsSpecial(text)) ? BitConverter.DoubleToUInt64Bits(value) : null;
    }

    private static bool IsBasedInteger(ReadOnlySpan<char> text)
    {
        var digits = WithoutSign(text);
        return digits.Length >= 2 && digits[0] == '0'
            && (IsHex(text) || digits.IndexOfAny('.', 'e', 'E') < 0);
    }
}
