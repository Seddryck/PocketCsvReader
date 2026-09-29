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
        // Returned spans cannot safely be backed by an array that has already
        // been returned to a pool. Allocate storage owned by the caller.
        var newArray = new T[newLength];
        var newSpan = newArray.AsSpan().Slice(0, newLength);
        prefix.CopyTo(newSpan);
        suffix.CopyTo(newSpan.Slice(prefix.Length));
        newSpan = newSpan.Slice(0, newLength);
        return newSpan;
    }

    public static Span<T> Concat<T>(this ReadOnlySpan<T> prefix, ReadOnlySpan<T> suffix, ArrayPool<T>? pool = null)
    {
        var newLength = prefix.Length + suffix.Length;
        var newArray = new T[newLength];
        var newSpan = newArray.AsSpan().Slice(0, newLength);
        prefix.CopyTo(newSpan);
        suffix.CopyTo(newSpan.Slice(prefix.Length));
        newSpan = newSpan.Slice(0, newLength);
        return newSpan;
    }
}
