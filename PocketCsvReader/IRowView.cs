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
}
