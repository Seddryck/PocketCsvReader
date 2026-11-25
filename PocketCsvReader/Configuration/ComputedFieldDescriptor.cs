namespace PocketCsvReader.Configuration;

internal abstract class ComputedFieldDescriptor(string name, Type type, string[] sources)
{
    internal FieldDescriptor Field { get; } = new(type, Name: name);
    internal string[] Sources { get; } = sources;

    internal abstract BoundComputedField Bind(int[] ordinals);
}

internal sealed class ComputedFieldDescriptor<T>(string name, string[] sources, ComputeField<T> compute)
    : ComputedFieldDescriptor(name, typeof(T), sources)
{
    internal override BoundComputedField Bind(int[] ordinals) => new BoundComputedField<T>(Field, ordinals, compute);
}

internal abstract class BoundComputedField(FieldDescriptor field)
{
    internal FieldDescriptor Field { get; } = field;

    internal abstract object? GetValue(CsvDataReader reader);
    internal abstract bool IsNull(CsvDataReader reader);
    internal abstract void Reset();
}

internal sealed class BoundComputedField<T>(FieldDescriptor field, int[] ordinals, ComputeField<T> compute)
    : BoundComputedField(field)
{
    private T _value = default!;
    private bool _hasValue;
    private bool _evaluating;

    internal T GetTypedValue(CsvDataReader reader)
    {
        if (_hasValue)
            return _value;
        if (_evaluating)
            throw new InvalidOperationException($"Computed field '{Field.Name}' recursively accessed its own value.");

        _evaluating = true;
        try
        {
            _value = compute(new ComputedFieldInputs(reader, ordinals));
            _hasValue = true;
            return _value;
        }
        finally
        {
            _evaluating = false;
        }
    }

    internal override object? GetValue(CsvDataReader reader) => GetTypedValue(reader);

    internal override bool IsNull(CsvDataReader reader) => GetTypedValue(reader) is null;

    internal override void Reset()
    {
        _hasValue = false;
        _value = default!;
    }
}
