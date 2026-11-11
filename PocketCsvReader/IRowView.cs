namespace PocketCsvReader;

/// <summary>A borrowed read-only row view, valid only during a filter call.</summary>
public interface IRowView
{
    int FieldCount { get; }
    string GetName(int ordinal);
    int GetOrdinal(string name);
    bool IsDBNull(int ordinal);
    bool IsDBNull(string name);
    object GetValue(int ordinal);
    object GetValue(string name);
    T GetFieldValue<T>(int ordinal);
    T GetFieldValue<T>(string name);
    T GetFieldValue<T>(int ordinal, ParseSpan<T> parser);
    T GetFieldValue<T>(string name, ParseSpan<T> parser);
}

/// <summary>A configured synchronous condition shared by all reader formats.</summary>
public sealed class ReaderFilter
{
    internal Func<IRowView, bool>? RowPredicate { get; }
    internal string? Column { get; }
    internal int Ordinal { get; }
    internal ParseSpan<bool>? SpanPredicate { get; }
    internal Func<IRowView, int, bool>? ValuePredicate { get; }

    internal ReaderFilter(Func<IRowView, bool> predicate) => RowPredicate = predicate;
    internal ReaderFilter(string? column, int ordinal, ParseSpan<bool>? span, Func<IRowView, int, bool>? value)
        => (Column, Ordinal, SpanPredicate, ValuePredicate) = (column, ordinal, span, value);

    internal static ParseSpan<bool> TextEquals(string expected, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!Enum.IsDefined(comparison)) throw new ArgumentOutOfRangeException(nameof(comparison));
        return value => value.Equals(expected.AsSpan(), comparison);
    }

    internal static ReaderFilter ForCell(string column, ParseSpan<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(predicate);
        return new(column, 0, predicate, null);
    }

    internal static ReaderFilter ForCell(int ordinal, ParseSpan<bool> predicate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentNullException.ThrowIfNull(predicate);
        return new(null, ordinal, predicate, null);
    }

    internal static ReaderFilter ForCell<T>(string column, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(predicate);
        return new(column, 0, null, (row, i) => Accept(row, i, predicate));
    }

    internal static ReaderFilter ForCell<T>(int ordinal, Func<T, bool> predicate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentNullException.ThrowIfNull(predicate);
        return new(null, ordinal, null, (row, i) => Accept(row, i, predicate));
    }

    private static bool Accept<T>(IRowView row, int ordinal, Func<T, bool> predicate)
        => !(typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null && row.IsDBNull(ordinal))
            && predicate(row.GetFieldValue<T>(ordinal));
}
