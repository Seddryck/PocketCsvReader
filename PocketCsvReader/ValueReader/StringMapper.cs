using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
public class StringMapper
{
    private const int InitialColumnCapacity = 32;
    private const int MaxHintedColumns = 16 * 1024;
    private const int MaxFieldLength = 128;
    private string?[]? _lastByColumn;

    protected PoolString FetchString { get; }
    private static readonly PoolString defaultPoolString = (span) => span.ToString();

    public StringMapper(PoolString? fetchString = null)
        => FetchString = fetchString ?? defaultPoolString;

    internal StringMapper(PoolString? fetchString, bool lastValueHint)
        : this(fetchString)
    {
        if (lastValueHint && fetchString is not null)
            _lastByColumn = new string?[InitialColumnCapacity];
    }

    internal string Map(int column, ReadOnlySpan<char> buffer)
        => _lastByColumn is { } values ? MapWithHint(column, buffer, values) : Map(buffer);

    private string MapWithHint(int column, ReadOnlySpan<char> buffer, string?[] lastByColumn)
    {
        if (buffer.IsEmpty || buffer.Length > MaxFieldLength || (uint)column >= MaxHintedColumns)
            return FetchString(buffer);

        if ((uint)column >= (uint)lastByColumn.Length)
            lastByColumn = GrowLastValues(column, lastByColumn);

        var previous = lastByColumn[column];
        if (previous is not null && buffer.SequenceEqual(previous.AsSpan()))
            return previous;

        var value = FetchString(buffer);
        // The returned string is an owned key only when the pool preserved the text.
        lastByColumn[column] = buffer.SequenceEqual(value.AsSpan()) ? value : null;
        return value;
    }

    private string?[] GrowLastValues(int column, string?[] values)
    {
        var capacity = Math.Min(MaxHintedColumns, Math.Max(values.Length * 2, column + 1));
        Array.Resize(ref values, capacity);
        return _lastByColumn = values;
    }

    public string Map(ReadOnlySpan<char> buffer)
        => FetchString(buffer);
}
