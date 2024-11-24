using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public static class SpanExtensions
{
    public static Span<T> Concat<T>(this Span<T> prefix, ReadOnlySpan<T> suffix, ArrayPool<T>? pool = null)
    {
        var newLength = prefix.Length + suffix.Length;
        var newArray = pool?.Rent(newLength) ?? new T[newLength];
        var newSpan = newArray.AsSpan().Slice(0, newLength);
        prefix.CopyTo(newSpan);
        suffix.CopyTo(newSpan.Slice(prefix.Length));
        newSpan = newSpan.Slice(0, newLength);
        pool?.Return(newArray);
        return newSpan;
    }

    public static Span<T> Concat<T>(this ReadOnlySpan<T> prefix, ReadOnlySpan<T> suffix, ArrayPool<T>? pool = null)
    {
        var newLength = prefix.Length + suffix.Length;
        var newArray = pool?.Rent(newLength) ?? new T[newLength];
        var newSpan = newArray.AsSpan().Slice(0, newLength);
        prefix.CopyTo(newSpan);
        suffix.CopyTo(newSpan.Slice(prefix.Length));
        newSpan = newSpan.Slice(0, newLength);
        pool?.Return(newArray);
        return newSpan;
    }
}
