using System.Globalization;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public partial class CsvDataReader
{
    private BoundComputedField[]? _computedFields;
    private int _computedFieldStart;

    private bool HasComputedFields => _computedFields is not null;

    private void BindComputedFields(string[] sourceFields)
    {
        if (Profile.Schema is not SchemaDescriptor.NamedSchemaDescriptor { ComputedFields.Length: > 0 } schema)
            return;

        var names = new HashSet<string>(sourceFields, StringComparer.Ordinal);
        var fields = new BoundComputedField[schema.ComputedFields.Length];
        var outputNames = new string[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var definition = schema.ComputedFields[i];
            var name = definition.Field.Name!;
            if (!names.Add(name))
                throw new InvalidDataException($"Computed field '{name}' conflicts with an existing column.");

            var ordinals = new int[definition.Sources.Length];
            for (var j = 0; j < ordinals.Length; j++)
            {
                var source = definition.Sources[j];
                ordinals[j] = Array.IndexOf(sourceFields, source, RowNumberWidth);
                if (ordinals[j] < 0)
                    throw new InvalidDataException($"Source column '{source}' for computed field '{name}' was not found.");
                if (ordinals[j] != Array.LastIndexOf(sourceFields, source))
                    throw new InvalidDataException($"Source column '{source}' for computed field '{name}' is ambiguous.");
            }

            fields[i] = definition.Bind(ordinals);
            outputNames[i] = name;
        }

        _computedFieldStart = Fields!.Length;
        _computedFields = fields;
        _sourceFields = sourceFields;
        Fields = _outputFields = [.. Fields, .. outputNames];
    }

    private BoundComputedField? GetComputedField(int ordinal)
    {
        if (_computedFields is null || _usingSourceFields)
            return null;

        var index = ordinal - _computedFieldStart;
        return (uint)index < (uint)_computedFields.Length ? _computedFields[index] : null;
    }

    internal NullableSpan GetComputedFieldInput(int sourceOrdinal)
    {
        if (_projectedOrdinals is null || _usingSourceFields)
            return GetValueOrThrow(sourceOrdinal);

        EnterSourceFields();
        try
        {
            return GetValueOrThrow(sourceOrdinal);
        }
        finally
        {
            ExitSourceFields();
        }
    }

    private void ResetComputedFields()
    {
        if (_computedFields is null)
            return;

        foreach (var field in _computedFields)
            field.Reset();
    }

    private protected override bool TryGetComputedValue<T>(int i, out T value)
    {
        var field = GetComputedField(i);
        if (field is null)
        {
            value = default!;
            return false;
        }

        EnsureValidationRecord();
        if (field is BoundComputedField<T> typed)
        {
            value = typed.GetTypedValue(this);
            return true;
        }
        else
        {
            var result = field.GetValue(this);
            if (result is T compatible)
            {
                value = compatible;
                return true;
            }
            if (result is null && default(T) is null)
            {
                value = default!;
                return true;
            }
        }

        throw new InvalidCastException($"Computed field '{field.Field.Name}' cannot be read as {typeof(T).Name}.");
    }

    protected override bool IsNull(int i)
    {
        if (GetComputedField(i) is not { } field)
            return base.IsNull(i);

        EnsureValidationRecord();
        return field.IsNull(this);
    }

    public override string GetString(int i)
        => GetComputedField(i) is null ? base.GetString(i) : GetStringValue(i);

    protected override string GetStringValue(int i)
    {
        if (GetComputedField(i) is not { } field)
            return base.GetStringValue(i);

        EnsureValidationRecord();
        var value = field.GetValue(this) ?? throw new InvalidCastException($"Computed field '{field.Field.Name}' is null.");
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
