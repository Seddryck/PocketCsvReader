namespace PocketCsvReader.Configuration;

internal abstract class ComputedFieldDescriptor(string name, Type type, string[]? sourceNames, int[]? sourceOrdinals)
{
    internal FieldDescriptor Field { get; } = new(type, Name: name);

    internal static ComputedFieldDescriptor Create<T>(string name, IReadOnlyList<string> sources, ComputeField<T> compute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(compute);

        var names = sources.ToArray();
        foreach (var source in names)
            ArgumentException.ThrowIfNullOrWhiteSpace(source, nameof(sources));
        return new ComputedFieldDescriptor<T>(name, names, null, compute);
    }

    internal static ComputedFieldDescriptor Create<T>(string name, IReadOnlyList<int> sources, ComputeField<T> compute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(compute);

        var ordinals = sources.ToArray();
        foreach (var ordinal in ordinals)
            ArgumentOutOfRangeException.ThrowIfNegative(ordinal, nameof(sources));
        return new ComputedFieldDescriptor<T>(name, null, ordinals, compute);
    }

    internal BoundComputedField Bind(string[] sourceFields, int offset)
    {
        if (sourceOrdinals is not null)
        {
            var names = Array.ConvertAll(sourceOrdinals, ordinal =>
                ordinal < sourceFields.Length - offset ? sourceFields[ordinal + offset] : null!);
            return Bind(sourceOrdinals, names);
        }

        var ordinals = new int[sourceNames!.Length];
        for (var j = 0; j < ordinals.Length; j++)
        {
            var source = sourceNames[j];
            var index = Array.IndexOf(sourceFields, source, offset);
            if (index >= 0 && Array.IndexOf(sourceFields, source, index + 1) >= 0)
                throw new InvalidDataException($"Source column '{source}' for computed field '{Field.Name}' is ambiguous.");
            ordinals[j] = index < 0 ? -1 : index - offset;
        }

        return Bind(ordinals, sourceNames);
    }

    protected abstract BoundComputedField Bind(int[] ordinals, string[] names);
}

internal sealed class ComputedFieldDescriptor<T>(string name, string[]? names, int[]? ordinals, ComputeField<T> compute)
    : ComputedFieldDescriptor(name, typeof(T), names, ordinals)
{
    protected override BoundComputedField Bind(int[] ordinals, string[] names)
        => new BoundComputedField<T>(Field, ordinals, names, compute);
}

internal abstract class BoundComputedField(FieldDescriptor field)
{
    internal FieldDescriptor Field { get; } = field;

    internal abstract object? GetValue(IComputedFieldSource reader);
    internal abstract bool IsNull(IComputedFieldSource reader);
    internal abstract void Reset();
}

internal sealed class BoundComputedField<T>(FieldDescriptor field, int[] ordinals, string[] names, ComputeField<T> compute)
    : BoundComputedField(field)
{
    private T _value = default!;
    private bool _isNull;
    private bool _hasValue;
    private bool _evaluating;

    private void Evaluate(IComputedFieldSource reader)
    {
        if (_hasValue)
            return;
        if (_evaluating)
            throw new InvalidOperationException($"Computed field '{Field.Name}' recursively accessed its own value.");

        _evaluating = true;
        try
        {
            foreach (var ordinal in ordinals)
            {
                if (!reader.GetComputedInput(ordinal).HasValue)
                {
                    _isNull = _hasValue = true;
                    return;
                }
            }

            _value = compute(new ComputedFieldInputs(reader, ordinals, names));
            _isNull = _value is null;
            _hasValue = true;
        }
        finally
        {
            _evaluating = false;
        }
    }

    internal T GetTypedValue(IComputedFieldSource reader)
    {
        Evaluate(reader);
        if (_isNull && default(T) is not null)
            throw new InvalidCastException($"Computed field '{Field.Name}' is null.");
        return _value;
    }

    internal override object? GetValue(IComputedFieldSource reader) => IsNull(reader) ? null : _value;

    internal override bool IsNull(IComputedFieldSource reader)
    {
        Evaluate(reader);
        return _isNull;
    }

    internal override void Reset()
    {
        _hasValue = false;
        _isNull = false;
        _value = default!;
    }
}
