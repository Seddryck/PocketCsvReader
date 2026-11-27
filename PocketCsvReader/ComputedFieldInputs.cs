namespace PocketCsvReader;

/// <summary>Computes a column value from the declared source fields in the current record.</summary>
public delegate T ComputeField<T>(ComputedFieldInputs fields);

/// <summary>A view of the declared source fields, valid during the computation.</summary>
public readonly ref struct ComputedFieldInputs
{
    private readonly IComputedFieldSource _reader;
    private readonly ReadOnlySpan<int> _ordinals;
    private readonly ReadOnlySpan<string> _names;

    internal ComputedFieldInputs(IComputedFieldSource reader, int[] ordinals, string[] names)
    {
        _reader = reader;
        _ordinals = ordinals;
        _names = names;
    }

    /// <summary>The number of declared source fields.</summary>
    /// <value>The number of columns in the source list.</value>
    public int Count => _ordinals.Length;

    /// <summary>Gets the sanitized text by zero-based position in the declared source list.</summary>
    public ReadOnlySpan<char> this[int index] => _reader.GetComputedInput(_ordinals[index]).Value;

    /// <summary>Tests whether a field at the specified position in the source list is null.</summary>
    public bool IsNull(int index) => !_reader.GetComputedInput(_ordinals[index]).HasValue;

    /// <summary>Gets the sanitized text of a declared field by zero-based source-record ordinal.</summary>
    /// <remarks>The span is valid during the callback. All declared inputs are present and non-null.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is outside the declared inputs.</exception>
    public NullableSpan GetSpan(int i) => _reader.GetComputedInput(RequireOrdinal(i));

    /// <summary>Parses a declared field by its zero-based source-record ordinal.</summary>
    public T GetFieldValue<T>(int i) => _reader.GetComputedInputValue<T>(RequireOrdinal(i));

    /// <summary>Parses a declared field by its source name.</summary>
    public T GetFieldValue<T>(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var index = _names.IndexOf(name);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(name), name, "The field is not a declared input.");
        return GetInputValue<T>(index);
    }

    internal T GetInputValue<T>(int index) => _reader.GetComputedInputValue<T>(_ordinals[index]);

    private int RequireOrdinal(int i)
    {
        if (!_ordinals.Contains(i))
            throw new ArgumentOutOfRangeException(nameof(i), i, "The ordinal is not a declared input.");
        return i;
    }
}

internal interface IComputedFieldSource
{
    NullableSpan GetComputedInput(int ordinal);
    T GetComputedInputValue<T>(int ordinal);
}
