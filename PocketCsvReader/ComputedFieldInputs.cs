namespace PocketCsvReader;

/// <summary>Computes a column value from the declared source fields in the current CSV record.</summary>
public delegate T ComputeField<T>(ComputedFieldInputs fields);

/// <summary>A view of sanitized source fields, in declaration order, valid during the computation.</summary>
public readonly ref struct ComputedFieldInputs
{
    private readonly CsvDataReader _reader;
    private readonly ReadOnlySpan<int> _ordinals;

    internal ComputedFieldInputs(CsvDataReader reader, ReadOnlySpan<int> ordinals)
    {
        _reader = reader;
        _ordinals = ordinals;
    }

    /// <summary>The number of declared source fields.</summary>
    /// <value>The number of columns in the source list.</value>
    public int Count => _ordinals.Length;

    /// <summary>Gets the sanitized text. A null field is returned as an empty span.</summary>
    /// <param name="index">The zero-based index in the source list.</param>
    /// <value>The field's text, or an empty span when the field is null.</value>
    public ReadOnlySpan<char> this[int index]
    {
        get
        {
            var value = _reader.GetComputedFieldInput(_ordinals[index]);
            return value.HasValue ? value.Value : default;
        }
    }

    /// <summary>Tests whether the sanitized source field has a null value.</summary>
    public bool IsNull(int index) => !_reader.GetComputedFieldInput(_ordinals[index]).HasValue;
}
