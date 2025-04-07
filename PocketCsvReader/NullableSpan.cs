using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;
public readonly ref struct NullableSpan
{
    private readonly ReadOnlySpan<char> span;
    private readonly bool hasValue;

    public NullableSpan(ReadOnlySpan<char> span)
    {
        this.span = span;
        this.hasValue = true;
    }

    public bool HasValue => hasValue;
    public ReadOnlySpan<char> Value => hasValue ? span : throw new InvalidOperationException("No value present.");

    public static implicit operator NullableSpan(ReadOnlyMemory<char>? memory) => memory is null ? default : new NullableSpan(memory.Value.Span);
    public static implicit operator NullableSpan(ReadOnlySpan<char> span) => new (span);
    public static implicit operator ReadOnlySpan<char>(NullableSpan nullable) => nullable.HasValue ? nullable.Value : throw new InvalidCastException("Null value cannot be cast to the expected type. Ensure the column does not contain NULL values.");
}
