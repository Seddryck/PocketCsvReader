using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace PocketCsvReader.Indexing;

internal static class BloomKeyHash
{
    internal const string Format = "xxhash64-seed0-canonical-key-v1";

    internal static ulong Compute(object value, StringComparison comparison, CompareInfo? compareInfo)
    {
        if (value is string text)
            return HashString(text, comparison, compareInfo);

        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        if (value is decimal number)
        {
            // G29 drops insignificant trailing zeros, including scale on zero.
            Span<byte> digits = stackalloc byte[64];
            number.TryFormat(digits, out var length, "G29", CultureInfo.InvariantCulture);
            return XxHash64.HashToUInt64(digits[..length]);
        }

        var integer = value switch
        {
            sbyte n => unchecked((ulong)n),
            short n => unchecked((ulong)n),
            int n => unchecked((ulong)n),
            long n => unchecked((ulong)n),
            byte n => n,
            ushort n => n,
            uint n => n,
            ulong n => n,
            DateOnly date => (ulong)date.DayNumber,
            _ => throw new NotSupportedException("This key type cannot be hashed for a Bloom index.")
        };
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, integer);
        return XxHash64.HashToUInt64(bytes);
    }

    private static ulong HashString(string text, StringComparison comparison, CompareInfo? compareInfo)
    {
        if (compareInfo is not null)
        {
            var options = comparison is StringComparison.CurrentCultureIgnoreCase or StringComparison.InvariantCultureIgnoreCase
                ? CompareOptions.IgnoreCase : CompareOptions.None;
            var length = compareInfo.GetSortKeyLength(text, options);
            byte[]? rented = length > 512 ? ArrayPool<byte>.Shared.Rent(length) : null;
            Span<byte> sortKey = rented is null ? stackalloc byte[length] : rented;
            try
            {
                var written = compareInfo.GetSortKey(text, sortKey, options);
                return XxHash64.HashToUInt64(sortKey[..written]);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        if (comparison == StringComparison.Ordinal)
            return HashUtf8(text);

        // OrdinalIgnoreCase compares as if both strings were uppercased invariantly.
        char[]? buffer = text.Length > 256 ? ArrayPool<char>.Shared.Rent(text.Length) : null;
        Span<char> upper = buffer is null ? stackalloc char[text.Length] : buffer;
        try
        {
            var written = text.AsSpan().ToUpperInvariant(upper);
            return HashUtf8(upper[..written]);
        }
        finally
        {
            if (buffer is not null)
                ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static ulong HashUtf8(ReadOnlySpan<char> text)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        byte[]? rented = length > 512 ? ArrayPool<byte>.Shared.Rent(length) : null;
        Span<byte> bytes = rented is null ? stackalloc byte[length] : rented;
        try
        {
            var written = Encoding.UTF8.GetBytes(text, bytes);
            return XxHash64.HashToUInt64(bytes[..written]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
