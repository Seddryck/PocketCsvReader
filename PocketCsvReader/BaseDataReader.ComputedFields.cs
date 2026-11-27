using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public abstract partial class BaseDataReader<TProfile> : IComputedFieldSource
{
    private BoundComputedField[]? _computedFields;
    private string[]? _computedSourceFields;
    private string[]? _computedOutputFields;
    private int _computedFieldStart;
    private bool _computedLayoutVisible;

    private protected virtual bool UsesCustomComputedLayout => false;
    private protected virtual bool InSourceFieldView => false;
    private protected virtual int ComputedSourceOffset => 0;

    protected int ComputedColumnCount => _computedLayoutVisible && !InSourceFieldView ? _computedFields!.Length : 0;
    private protected bool HasComputedFields => _computedFields is not null;

    private protected void BindComputedFields(string[] sourceFields)
    {
        if (Profile.Schema is not { ComputedFields.Length: > 0 } schema)
            return;

        if (!ReferenceEquals(sourceFields, _computedSourceFields))
        {
            var names = new HashSet<string>(sourceFields, StringComparer.Ordinal);
            var fields = new BoundComputedField[schema.ComputedFields.Length];
            var outputNames = new string[fields.Length];
            for (var i = 0; i < fields.Length; i++)
            {
                var definition = schema.ComputedFields[i];
                var name = definition.Field.Name!;
                if (!names.Add(name))
                    throw new InvalidDataException($"Computed field '{name}' conflicts with an existing column.");

                fields[i] = definition.Bind(sourceFields, ComputedSourceOffset);
                outputNames[i] = name;
            }

            _computedFieldStart = Fields!.Length;
            _computedFields = fields;
            _computedSourceFields = sourceFields;
            _computedOutputFields = [.. Fields, .. outputNames];
        }

        Fields = _computedOutputFields;
        _computedLayoutVisible = true;
    }

    private void RestoreComputedSourceLayout()
    {
        if (_computedLayoutVisible && !UsesCustomComputedLayout)
        {
            Fields = _computedSourceFields;
            _computedLayoutVisible = false;
        }
    }

    private void CompleteComputedLayout()
    {
        if (Profile.Schema is { ComputedFields.Length: > 0 } && !UsesCustomComputedLayout && Fields is { } sourceFields)
            BindComputedFields(sourceFields);
    }

    private protected BoundComputedField? GetComputedField(int ordinal)
    {
        if (!_computedLayoutVisible || InSourceFieldView)
            return null;

        var index = ordinal - _computedFieldStart;
        return (uint)index < (uint)_computedFields!.Length ? _computedFields[index] : null;
    }

    /// <summary>Rejects raw-text access to computed columns.</summary>
    protected void RequireSourceColumn(int ordinal)
    {
        if (GetComputedField(ordinal) is not null)
            throw new NotSupportedException("Computed fields have no raw source text.");
    }

    NullableSpan IComputedFieldSource.GetComputedInput(int ordinal)
    {
        if (ordinal < 0)
            return default;

        var restore = UsesCustomComputedLayout && !InSourceFieldView;
        if (restore)
            EnterSourceFields();
        try
        {
            var i = ordinal + ComputedSourceOffset;
            return (uint)i < (uint)FieldCount && GetRecordField(i) is { Value.IsNull: false }
                ? GetValueOrThrow(i)
                : default;
        }
        finally
        {
            if (restore)
                ExitSourceFields();
        }
    }

    T IComputedFieldSource.GetComputedInputValue<T>(int ordinal)
    {
        var restore = UsesCustomComputedLayout && !InSourceFieldView;
        if (restore)
            EnterSourceFields();
        try
        {
            return GetFieldValue<T>(ordinal + ComputedSourceOffset);
        }
        finally
        {
            if (restore)
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

    protected override bool TryGetFieldDescriptor(int i, [NotNullWhen(true)] out FieldDescriptor? field)
    {
        if (GetComputedField(i) is { } computed)
        {
            field = computed.Field;
            return true;
        }
        return base.TryGetFieldDescriptor(i, out field);
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

        throw new InvalidCastException($"Computed field '{field.Field.Name}' cannot be read as {typeof(T).Name}.");
    }

    public override object GetValue(int i)
        => TryGetComputedValue<object>(i, out var value) ? value ?? DBNull.Value : base.GetValue(i);

    public override bool IsValid(int i)
    {
        if (GetComputedField(i) is not { } field)
            return base.IsValid(i);

        EnsureValidationRecord();
        try { _ = field.IsNull(this); return true; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    private protected override bool TryGetComputedNull(int i, out bool isNull)
    {
        if (GetComputedField(i) is not { } field)
        {
            isNull = false;
            return false;
        }

        EnsureValidationRecord();
        isNull = field.IsNull(this);
        return true;
    }

    public override string GetString(int i)
    {
        if (GetComputedField(i) is not { } field)
            return base.GetString(i);

        EnsureValidationRecord();
        var value = field.GetValue(this) ?? throw new InvalidCastException($"Computed field '{field.Field.Name}' is null.");
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
