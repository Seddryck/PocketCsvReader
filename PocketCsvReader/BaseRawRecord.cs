using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;
public abstract class BaseRawRecord<TProfile>
    where TProfile : IProfile
{
    protected TProfile Profile { get; }
    protected StringMapper StringMapper { get; }
    public int RowCount { get; protected set; } = 0;
    public string[]? Fields { get; protected set; } = null;
    private RecordMemory? _record;
    private CurrentRecord _current;
    private bool _copyMetadataOnMaterialization;
    private Utf8RecordSource? _deferredRecord;
    private int _deferredFieldCount;
    protected RecordMemory? Record
    {
        get
        {
            EnsureCurrentRecord();
            return _record ?? (!_current.HasFields ? null : _record = _current.Materialize(_copyMetadataOnMaterialization));
        }
        set
        {
            _record = value;
            _current = value?.Current ?? default;
            _deferredRecord = null;
            _copyMetadataOnMaterialization = false;
        }
    }
    protected ReadOnlyMemory<char> CurrentRecordMemory
    {
        get
        {
            EnsureCurrentRecord();
            return _current.IsUtf8 ? Record!.Span : _current.Characters;
        }
    }
    protected FieldSpan[]? CurrentFieldSpans
    {
        get
        {
            EnsureCurrentRecord();
            var fields = _current.BorrowFields();
            if (fields is not null)
                _current = _current.WithExpandedFields(fields);
            return fields;
        }
    }

    private protected bool HasCurrentFields => _deferredRecord is not null || _current.HasFields;
    private protected int CurrentFieldSpanCount => _deferredRecord is not null ? _deferredFieldCount : _current.FieldCount;

    private protected bool IsCurrentFieldEscaped(int ordinal)
    {
        EnsureCurrentRecord();
        return _current.IsEscaped(ordinal);
    }

    private protected FieldSpan GetCurrentField(int ordinal)
    {
        EnsureCurrentRecord();
        return _current.GetField(ordinal);
    }

    /// <summary>Reads a field without constructing a public record wrapper.</summary>
    protected ReadOnlyMemory<char> GetCurrentFieldMemory(int ordinal)
    {
        EnsureCurrentRecord();
        return _current.Slice(ordinal);
    }

    private protected bool TryGetCurrentUtf8(int ordinal, out ReadOnlyMemory<byte> value, out bool isEscaped)
    {
        EnsureCurrentRecord();
        return _current.TryGetUtf8(ordinal, out value, out isEscaped);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected OwnedCsvRecord CopyCurrentCsvRecord(int[]? selectedOrdinals, out int sourceFieldCount)
    {
        EnsureCurrentRecord();
        sourceFieldCount = _current.FieldCount;
        return _current.CopyForBatch(selectedOrdinals);
    }

    private void EnsureCurrentRecord()
    {
        if (_deferredRecord is { } source)
            MaterializeCurrentRecord(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MaterializeCurrentRecord(Utf8RecordSource source)
    {
        _current = source.GetIndexedRecord();
        _deferredRecord = null;
    }

    private protected void SetDeferredRecord(Utf8RecordSource source, int fieldCount)
    {
        _record = null;
        _deferredFieldCount = fieldCount;
        _deferredRecord = source;
        _copyMetadataOnMaterialization = true;
    }

    private protected void InvalidateDeferredRecord()
    {
        if (_deferredRecord is null)
            return;

        _deferredRecord = null;
        _current = default;
    }

    private protected void SetRecord(CurrentRecord record, bool copyMetadataOnMaterialization = false)
    {
        _record = null;
        _current = record;
        _deferredRecord = null;
        _copyMetadataOnMaterialization = copyMetadataOnMaterialization;
    }

    protected BaseRawRecord(TProfile profile, StringMapper stringMapper)
    {
        Profile = profile;
        StringMapper = stringMapper;
    }

    protected void SetRecord(ReadOnlyMemory<char> memory, FieldSpan[] fieldSpans)
    {
        SetRecord(new CurrentRecord(memory, fieldSpans));
    }

    public abstract int FieldCount { get; }

    public virtual string GetName(int i)
        => Fields?[i] ?? throw new InvalidOperationException("Fields are not defined yet.");

    public virtual int GetOrdinal(string name)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");
        var index = Array.IndexOf(Fields, name);
        if (index < 0)
            throw new ArgumentOutOfRangeException($"Field '{name}' not found.");
        return index;
    }

    /// <summary>
    /// Attempts to find the zero-based ordinal of a field by name.
    /// </summary>
    /// <param name="name">The field name to find.</param>
    /// <param name="ordinal">
    /// When this method returns, contains the zero-based field ordinal if the field was found;
    /// otherwise, <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> if a field with the specified name was found; otherwise, <c>false</c>.
    /// </returns>
    public virtual bool TryGetOrdinal(string name, out int? ordinal)
    {
        var index = Fields is null ? -1 : Array.IndexOf(Fields, name);
        ordinal = index >= 0 ? index : null;
        return ordinal.HasValue;
    }

    public string GetDataTypeName(int i)
    {
        if (TryGetFieldDescriptor(i, out var field))
            return field.DataSourceTypeName;
        return string.Empty;
    }

    public Type GetFieldType(int i)
    {
        if (TryGetFieldDescriptor(i, out var field))
            return field.RuntimeType;
        return typeof(object);
    }

    protected virtual int GetSchemaOrdinal(int i) => i;

    private protected virtual FieldSpan? GetRecordField(int i)
        => i >= CurrentFieldSpanCount ? null : GetCurrentField(i);

    protected virtual FieldDescriptor GetFieldDescriptor(int i)
    {
        if (Profile.Schema is null)
            throw new InvalidOperationException("Schema is not defined.");

        if (Profile.Schema.IsMatchingByName)
        {
            if (Fields is null)
                throw new InvalidOperationException("Fields are not defined yet.");

            var headerName = GetName(i);
            if (Profile.Schema.Fields.TryGetValue(headerName, out var field))
                return field;
            throw new ArgumentOutOfRangeException($"Field index '{i}' is linked to header '{headerName}' but there is no corresponding field in the schema.");
        }

        if (Profile.Schema.IsMatchingByIndex)
        {
            i = GetSchemaOrdinal(i);
            if (i < Profile.Schema.Fields.Length)
                return Profile.Schema.Fields[i];
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        }

        throw new NotImplementedException("Schema matching is not defined.");
    }

    protected virtual bool TryGetFieldDescriptor(int i, [NotNullWhen(true)] out FieldDescriptor? field)
    {
        if (Profile.Schema is null)
        {
            field = null;
            return false;
        }

        if (Profile.Schema.IsMatchingByName)
        {
            if (Fields is null)
                throw new InvalidOperationException("Fields are not defined yet.");
            return Profile.Schema.Fields.TryGetValue(GetName(i), out field);
        }

        if (Profile.Schema.IsMatchingByIndex)
        {
            i = GetSchemaOrdinal(i);
            field = i < Profile.Schema.Fields.Length
                        ? Profile.Schema.Fields[i] : null;
            return field is not null;
        }
        throw new NotImplementedException("Schema matching is not defined.");
    }

#if DEBUG
    /// <summary>
        /// Determines whether the field at the specified index was quoted in the raw CSV record.
        /// </summary>
        /// <param name="i">The zero-based index of the field.</param>
        /// <returns>True if the field was quoted; otherwise, false.</returns>
    public bool IsQuotedField(int i)
        => GetRecordField(i)!.Value.Value.WasQuoted;

    /// <summary>
        /// Determines whether the field at the specified index contains escaped characters in the raw record.
        /// </summary>
        /// <param name="i">The zero-based index of the field.</param>
        /// <returns>True if the field contains escaped characters; otherwise, false.</returns>
    public bool IsEscapedField(int i)
        => GetRecordField(i)!.Value.Value.IsEscaped;
#endif

    /// <summary>
/// Returns the raw string value of the field at the specified index.
/// </summary>
/// <param name="i">The zero-based index of the field.</param>
/// <returns>The raw string representation of the field value.</returns>
    public abstract string GetRawString(int i);

    protected virtual bool IsNull(int i)
        => !GetValueOrThrow(i).HasValue;
    public virtual string GetString(int i)
        => GetDecodedString(i);

    /// <summary>Returns the field text after format decoding and sanitization, before value conversion.</summary>
    /// <param name="ordinal">The zero-based index of the field.</param>
    /// <returns>The decoded field text.</returns>
    /// <exception cref="InvalidCastException">The field is null.</exception>
    public string GetDecodedString(int ordinal)
    {
        EnsureCurrentRecord();
        return _current.IsUtf8 && TryGetDirectString(ordinal, out var value)
            ? value : GetStringValue(ordinal);
    }

    protected virtual string GetStringValue(int i)
        => StringMapper.Map(i, GetValueOrThrow(i))!;

    /// <summary>Returns a field's logical text, allowing formats to decode and cache it on demand.</summary>
    protected virtual ReadOnlyMemory<char> GetFieldMemory(ref FieldSpan field)
        => field.DecodedValue is { } decoded
            ? decoded.AsMemory()
            : CurrentRecordMemory.Slice(field.Value.Start, field.Value.Length);

    private protected virtual bool TryGetDirectString(int i, out string value)
    {
        value = null!;
        return false;
    }

    protected virtual IFormatDescriptor GetFormat(int i)
    {
        if (!TryGetFieldDescriptor(i, out var field))
            return IFormatDescriptor.None;
        return field.Format ?? IFormatDescriptor.None;
    }
    protected abstract NullableSpan GetValueOrThrow(int i);
}
