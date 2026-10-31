using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using PocketCsvReader.Configuration;
using System.Reflection;
using PocketCsvReader.FieldParsing;
using PocketCsvReader.CharParsing;
using System.Linq.Expressions;
using System.ComponentModel.Design;

namespace PocketCsvReader;
public abstract class BaseDataRecord<P> : BaseRawRecord<P>, IDataRecord, IFieldValueReader where P : IProfile
{
    private SpanParser Parser { get; } = new();
    // Schema descriptors outlive row shapes. Keep compiled delegates while rebinding ordinals.
    private readonly Dictionary<(FieldDescriptor Field, Type Type), SpanParser.ParserEntry> _schemaParsers = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<BaseDataRecord<P>, int, object>> ArrayReaders = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<BaseDataRecord<P>, int, bool>> ArrayValidators = new();
    private Dictionary<int, ISanitizer>? _sanitizers;
    private SanitizerFactory? _sanitizerFactory;

    protected BaseDataRecord(P profile, StringMapper stringMapper)
        : base(profile, stringMapper)
    {
        foreach (var parser in profile.Parsers?.SpanParsers ?? [])
            Parser.Register(parser.Key, parser.Value);
    }

    protected ISanitizer GetSanitizer(int ordinal, ParserOptimizationOptions parserOptimizations)
    {
        if (_sanitizers?.TryGetValue(ordinal, out var sanitizer) is true)
            return sanitizer;

        _sanitizerFactory ??= new SanitizerFactory(parserOptimizations);
        sanitizer = _sanitizerFactory.Create(
            SequenceCollection.Concat(
                Profile.Resource?.Sequences,
                (Profile.Schema is null ? null : GetFieldDescriptor(ordinal))?.Sequences),
            CreateFieldEscaper());
        (_sanitizers ??= []).Add(ordinal, sanitizer);
        return sanitizer;
    }

    private protected virtual FieldEscaper? CreateFieldEscaper() => null;

    protected virtual bool CacheInferredFieldParsers => false;

    protected void ResetFieldCaches()
    {
        Parser.ClearFieldParsers();
        _sanitizers?.Clear();
    }

    public object this[int i]
        => GetValue(i);
    public object this[string name]
        => GetValue(GetOrdinal(name));
    public bool GetBoolean(int i)
        => GetNumeric<bool>(i);
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i) => throw new NotImplementedException();

    public DateTime GetDateTime(int i)
        => GetTemporal<DateTime>(i);

    public DateOnly GetDate(int i)
        => GetTemporal<DateOnly>(i);

    public TimeOnly GetTime(int i)
        => GetTemporal<TimeOnly>(i);

    public DateTimeOffset GetDateTimeOffset(int i)
        => GetTemporal<DateTimeOffset>(i);

    protected T GetTemporal<T>(int i)
    {
        try
        {
            if (Parser.TryGetParser<T>(i, out var parse))
                return parse(GetValueOrThrow(i));

            if (TryGetFieldDescriptor(i, out var field) && (field.SpanParse is not null || field.Parse is not null || field.Format is TemporalFormatDescriptor))
            {
                if (field.SpanParse is not null || field.Parse is not null)
                    RegisterFieldParser(i, field);
                else
                    RegisterFormattedParser(i, typeof(T), field);
                return Parser.Parse<T>(i, GetValueOrThrow(i));
            }

            return ParseAndOptionallyCache<T>(i);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    public Guid GetGuid(int i)
    {
        try
        {
            if (Parser.TryGetParser<Guid>(i, out var parse))
                return parse(GetValueOrThrow(i));

            return ParseAndOptionallyCache<Guid>(i);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    public decimal GetDecimal(int i)
        => GetNumeric<decimal>(i);

    public double GetDouble(int i)
        => GetNumeric<double>(i);

    public float GetFloat(int i)
        => GetNumeric<float>(i);

    public short GetInt16(int i)
        => GetNumeric<short>(i);

    public int GetInt32(int i)
        => GetNumeric<int>(i);

    public long GetInt64(int i)
        => GetNumeric<long>(i);

    protected T GetNumeric<T>(int i)
    {
        if (TryGetDirectValue<T>(i, out var direct)) return direct;
        try
        {
            if (Parser.TryGetParser<T>(i, out var parse))
                return parse(GetValueOrThrow(i));

            if (TryGetFieldDescriptor(i, out var field) && (field.SpanParse is not null || field.Parse is not null || field.Format is NumericFormatDescriptor))
            {
                if (field.SpanParse is not null || field.Parse is not null)
                    RegisterFieldParser(i, field);
                else
                    RegisterFormattedParser(i, typeof(T), field);
                return Parser.Parse<T>(i, GetValueOrThrow(i));
            }

            return ParseAndOptionallyCache<T>(i);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    private protected virtual bool TryGetDirectValue<T>(int i, out T value)
    {
        value = default!;
        return false;
    }

    protected virtual object GetMissingField()
        => string.Empty;

    /// <summary>Checks whether the current cell can be read using its declared schema and parsing rules.</summary>
    /// <returns>False for conversion failures; true for readable values, including unparsed text.</returns>
    /// <remarks>
    /// Reuses cached TryParse delegates without advancing the reader or populating getter bindings.
    /// Null and missing cells follow GetValue: readable null/default values are valid, rejected nulls are not.
    /// Usage errors, invalid configuration, and unexpected parser failures propagate.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is outside the current record.</exception>
    /// <exception cref="InvalidOperationException">No current record is available.</exception>
    public virtual bool IsValid(int i)
    {
        EnsureValidationRecord();
        if (i < 0 || i >= FieldCount)
            throw new ArgumentOutOfRangeException(nameof(i), i, "Field index is out of range.");

        TryGetFieldDescriptor(i, out var field);
        if (field is not null && field.RuntimeType.IsArray && !HasCustomParser(field)
            && !Parser.TryGetEntry(field.RuntimeType, out _))
            return ArrayValidators.GetOrAdd(field.RuntimeType, CreateArrayValidator)(this, i);
        if (i >= CurrentFieldSpans!.Length)
        {
            _ = GetMissingField();
            return true;
        }

        var value = GetValueOrThrow(i);
        if (!value.HasValue)
        {
            try { _ = GetNullValue(i); return true; }
            catch (InvalidCastException) { return false; }
        }

        SpanParser.ParserEntry? entry;
        if (field is null)
            Parser.TryGetEntry(i, out entry);
        else if (HasCustomParser(field))
            entry = GetSchemaParser(i, field);
        else if (!Parser.TryGetEntry(field.RuntimeType, out entry)
            && Nullable.GetUnderlyingType(field.RuntimeType) is { } underlyingType)
            Parser.TryGetEntry(underlyingType, out entry);
        return entry?.IsValid(value.Value) ?? true;
    }

    /// <summary>Validates the named current cell using the same rules as <see cref="IsValid(int)"/>.</summary>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The name does not exist in the current record.</exception>
    /// <exception cref="InvalidOperationException">No current record is available.</exception>
    public bool IsValid(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureValidationRecord();
        return IsValid(GetOrdinal(name));
    }

    protected virtual void EnsureValidationRecord()
    {
        if (CurrentFieldSpans is null)
            throw new InvalidOperationException("No current record is available.");
    }

    private static Func<BaseDataRecord<P>, int, bool> CreateArrayValidator(Type arrayType)
    {
        if (!arrayType.IsSZArray)
            throw new NotSupportedException("Schema arrays must be one-dimensional, zero-based arrays.");
        var method = typeof(BaseDataRecord<P>).GetMethod(nameof(IsValidArray), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(arrayType.GetElementType()!);
        return method.CreateDelegate<Func<BaseDataRecord<P>, int, bool>>();
    }

    private bool IsValidArray<T>(int i)
    {
        var field = GetArrayField(i);
        if (!field.HasValue)
            return true;
        if (!Parser.TryGetTryParser<T>(i, out var validate) && !Parser.TryGetTryParser(out validate))
            throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
        foreach (var child in field.Value.Children!)
            if (!child.Value.IsNull && !validate(GetFieldSpan(child), out _))
                return false;
        return true;
    }

    public virtual object GetValue(int i)
    {
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        if (TryGetFieldDescriptor(i, out var field) && field.RuntimeType.IsArray
            && !HasCustomParser(field) && !Parser.TryGetParser(field.RuntimeType, out _))
            return ArrayReaders.GetOrAdd(field.RuntimeType, CreateArrayReader)(this, i);
        if (i >= CurrentFieldSpans!.Length)
            return GetMissingField();

        var value = GetValueOrThrow(i);
        if (!value.HasValue)
            return GetNullValue(i);

        var parse = ResolveValueParser(i);
        if (parse is null)
            return StringMapper.Map(value.Value);

        try
        {
            return parse.Invoke(value.Value);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    protected virtual object GetNullValue(int i)
        => throw new InvalidCastException($"Field index '{i}' is null.");

    private static Func<BaseDataRecord<P>, int, object> CreateArrayReader(Type arrayType)
    {
        if (!arrayType.IsSZArray)
            throw new NotSupportedException("Schema arrays must be one-dimensional, zero-based arrays.");

        // Close and bind the public typed path once per element type.
        Expression<Func<BaseDataRecord<P>, int, object>> template = (record, index) => record.GetArray<object>(index);
        var call = (MethodCallExpression)template.Body;
        var elementType = arrayType.GetElementType() ?? throw new ArgumentException("An array element type is required.", nameof(arrayType));
        var method = call.Method.GetGenericMethodDefinition().MakeGenericMethod(elementType);
        return Expression.Lambda<Func<BaseDataRecord<P>, int, object>>(
            Expression.Call(template.Parameters[0], method, template.Parameters[1]), template.Parameters).Compile();
    }

    private ParseSpan<object>? ResolveValueParser(int i)
    {
        if (Parser.TryGetParser(i, out var parse))
            return parse;
        if (!TryGetFieldDescriptor(i, out var field))
            return null;
        return ResolveFieldParser(i, field);
    }

    private ParseSpan<object>? ResolveFieldParser(int i, FieldDescriptor field)
    {
        if (HasCustomParser(field))
            return RegisterFieldParser(i, field).Boxed;
        if (!Parser.TryGetParser(field.RuntimeType, out ParseSpan<object>? parse)
            && Nullable.GetUnderlyingType(field.RuntimeType) is { } underlyingType)
            Parser.TryGetParser(underlyingType, out parse);
        return parse;
    }

    private static bool HasCustomParser(FieldDescriptor field)
        => field.SpanParse is not null || field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor);

    private SpanParser.ParserEntry RegisterFieldParser(int i, FieldDescriptor field)
    {
        var entry = GetSchemaParser(i, field);
        Parser.BindFieldParser(i, entry);
        return entry;
    }

    private SpanParser.ParserEntry GetSchemaParser(int i, FieldDescriptor field)
    {
        var key = (field, field.RuntimeType);
        if (!_schemaParsers.TryGetValue(key, out var entry))
        {
            if (field.SpanParse is not null || field.Parse is not null)
                entry = SpanParser.CreateEntry(field.RuntimeType,
                    field.SpanParse ?? SpanParser.Adapt(field.RuntimeType, field.Parse!));
            else if (field.RuntimeType == typeof(string))
            {
                ParseSpan<string> parse = span => span.ToString();
                entry = SpanParser.CreateEntry(parse);
            }
            else if ((field.Format is not null && field.Format is not NoneFormatDescriptor) || field.RuntimeType != typeof(object))
                entry = CreateParser(field.RuntimeType, field);
            else
                throw new ArgumentException($"Field descriptor for index '{i}' is missing both the Parse function and the Format property.");

            _schemaParsers.Add(key, entry);
        }
        return entry;
    }

    private void RegisterFormattedParser(int i, Type type, FieldDescriptor field)
    {
        var key = (field, type);
        if (!_schemaParsers.TryGetValue(key, out var entry))
        {
            entry = CreateParser(type, field);
            _schemaParsers.Add(key, entry);
        }
        Parser.BindFieldParser(i, entry);
    }

    public object GetValue(string name)
        => GetValue(GetOrdinal(name));

    protected virtual bool IsNullFieldValue<T>(int i)
    {
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");

        if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
            return IsDBNull(i);
        return false;
    }

    public T GetFieldValue<T>(int i)
    {
        if (TryGetDirectValue<T>(i, out var direct)) return direct;
        if (IsNullFieldValue<T>(i))
            return default!;
        try
        {
            if (Parser.TryGetParser<T>(i, out var parse))
                return parse(GetValueOrThrow(i));
            if (TryGetFieldDescriptor(i, out var field) && HasCustomParser(field))
            {
                RegisterFieldParser(i, field);
                return Parser.Parse<T>(i, GetValueOrThrow(i));
            }
            return ParseAndOptionallyCache<T>(i);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }

        throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
    }

    private T ParseAndOptionallyCache<T>(int i)
    {
        if (!CacheInferredFieldParsers)
            return Parser.Parse<T>(GetValueOrThrow(i));

        Parser.CacheTypeParser<T>(i);
        return Parser.Parse<T>(i, GetValueOrThrow(i));
    }

    public T GetFieldValue<T>(int i, IFormatProvider format) where T : IParsable<T>
    {
        if (IsNullFieldValue<T>(i))
            return default!;

        return T.Parse(GetValueOrThrow(i).Value.ToString(), format);
    }

    public T GetFieldValue<T>(int i, string pattern, IFormatProvider? format = null) where T : IParsable<T>
    {
        if (IsNullFieldValue<T>(i))
            return default!;

        var locator = new TypeParserLocator<T>();
        var func = locator.LocateSpanParser([pattern, format ?? CultureInfo.InvariantCulture]);

        return func(GetValueOrThrow(i).Value);
    }

    /// <summary>Parses a sanitized field synchronously without materializing a string.</summary>
    /// <remarks>Use a typed span lambda or a ParseSpan delegate to select this overload.</remarks>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public T GetFieldValue<T>(int i, ParseSpan<T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (IsNullFieldValue<T>(i))
            return default!;
        return parse(GetValueOrThrow(i).Value);
    }

    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public T GetFieldValue<T>(string name, ParseSpan<T> parse)
        => GetFieldValue(GetOrdinal(name), parse);

    public T GetFieldValue<T>(int i, Func<string, T> parse)
    {
        if (IsNullFieldValue<T>(i))
            return default!;

        return parse(GetValueOrThrow(i).Value.ToString());
    }

    public T GetFieldValue<T>(string name)
        => GetFieldValue<T>(GetOrdinal(name));

    public T GetFieldValue<T>(string name, IFormatProvider format) where T : IParsable<T>
        => GetFieldValue<T>(GetOrdinal(name), format);

    public T GetFieldValue<T>(string name, string pattern, IFormatProvider? format = null) where T : IParsable<T>
        => GetFieldValue<T>(GetOrdinal(name), pattern, format);

    public T GetFieldValue<T>(string name, Func<string, T> parse)
        => GetFieldValue(GetOrdinal(name), parse);

    public int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var length = Math.Min(values.Length, FieldCount);

        for (int i = 0; i < length; i++)
            values[i] = GetValue(i);
        return length;
    }

    public bool IsDBNull(int i)
        => IsNull(i);

    /// <summary>
    /// Determines whether the field with the specified name contains a null value.
    /// </summary>
    /// <param name="name">The name of the field.</param>
    /// <returns><c>true</c> if the field contains a null value; otherwise, <c>false</c>.</returns>
    public bool IsDBNull(string name)
        => IsDBNull(GetOrdinal(name));

    /// <summary>
    /// Parses the field at the specified index as an array of nullable values of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="i">The zero-based index of the field to parse as an array.</param>
    /// <returns>An array of nullable <typeparamref name="T"/> values parsed from the field's child spans.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the field index is out of range.</exception>
    /// <exception cref="NotImplementedException">Thrown if the field does not have child spans.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no suitable parser is registered or can be created for the specified type.</exception>
    public T?[] GetArray<T>(int i)
    {
        var field = GetArrayField(i);
        if (!field.HasValue)
            return [];

        var children = field.Value.Children!;
        var parse = ResolveArrayParser<T>(i);
        var array = (T?[])Array.CreateInstance(typeof(T?), children.Length);
        for (int j = 0; j < children.Length; j++)
        {
            var child = children[j];
            array[j] = child.Value.IsNull
                        ? default
                        : parse(GetFieldSpan(child));
        }
        return array;
    }

    /// <summary>
    /// Returns an array of objects parsed from the child spans of the field at the specified index.
    /// </summary>
    /// <param name="i">The zero-based index of the field containing the array.</param>
    /// <returns>An array of objects representing the parsed values of the field's children, or an empty array if the field has no children.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the field index is out of range.</exception>
    /// <exception cref="NotImplementedException">Thrown if the field does not support child spans.</exception>
    public object?[] GetArray(int i)
    {
        var field = GetArrayField(i);
        if (!field.HasValue)
            return [];

        var children = field.Value.Children!;
        var parse = ResolveArrayParser(i);
        var array = (object?[])Array.CreateInstance(typeof(object), children.Length);
        for (int j = 0; j < children.Length; j++)
        {
            var child = children[j];
            array[j] = child.Value.IsNull
                ? null
                : parse(GetFieldSpan(child));
        }
        return array;
    }

    /// <summary>
    /// Retrieves the parsed value of type <typeparamref name="T"/> from the <paramref name="j"/>th child element of the field at index <paramref name="i"/>.
    /// </summary>
    /// <param name="i">The zero-based index of the field containing the array.</param>
    /// <param name="j">The zero-based index of the array element within the field.</param>
    /// <returns>The parsed value of type <typeparamref name="T"/>, or <c>default</c> if the element is null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="i"/> is out of range, or if the field does not contain an item at position <paramref name="j"/>.
    /// </exception>
    /// <exception cref="NotImplementedException">
    /// Thrown if the field at index <paramref name="i"/> does not support child elements.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no suitable parser is registered or can be created for type <typeparamref name="T"/>.
    /// </exception>
    public T? GetArrayItem<T>(int i, int j)
    {
        var field = GetArrayField(i);
        if (!field.HasValue)
            throw new ArgumentOutOfRangeException($"Field index '{i}' doesn't contain an item at position '{j}'.");

        var children = field.Value.Children!;
        if (j >= children.Length)
            throw new ArgumentOutOfRangeException($"Field index '{i}' doesn't contain an item at position '{j}'.");

        var parse = ResolveArrayParser<T>(i);
        var child = children[j];
        return child.Value.IsNull
                ? default
                : parse(GetFieldSpan(child));
    }

    private FieldSpan? GetArrayField(int i)
    {
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        if (i >= CurrentFieldSpans!.Length)
            return null;

        var field = CurrentFieldSpans[i];
        if (field.Children is null)
            throw new NotImplementedException();
        return field;
    }

    private ParseSpan<T> ResolveArrayParser<T>(int i)
    {
        if (Parser.TryGetParser<T>(i, out var parse))
            return parse;
        if (TryGetFieldDescriptor(i, out var field) && HasCustomParser(field))
        {
            RegisterFieldParser(i, field);
            return Parser.GetParser<T>(i);
        }
        if (Parser.TryGetParser(out parse))
            return parse;
        throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
    }

    private ParseSpan<object> ResolveArrayParser(int i)
    {
        if (Parser.TryGetParser(i, out var parse))
            return parse;
        if (TryGetFieldDescriptor(i, out var field) && HasCustomParser(field))
        {
            RegisterFieldParser(i, field);
            if (Parser.TryGetParser(i, out parse))
                return parse;
        }
        if (field is not null && Parser.TryGetParser(field.RuntimeType, out parse))
            return parse;
        if (Parser.TryGetParser(out parse))
            return parse;
        return (ReadOnlySpan<char> span) => span.ToString();
    }

    private ReadOnlySpan<char> GetFieldSpan(FieldSpan field)
    {
        if (field.DecodedValue is not null)
            return field.DecodedValue.AsSpan();
        if (CurrentFieldSpans is null)
            throw new InvalidOperationException("Current record is not set.");
        return CurrentRecordMemory.Span.Slice(field.Value.Start, field.Value.Length);
    }

    /// <summary>
    /// Creates cached parsing delegates for the specified type using the field's format descriptor.
    /// </summary>
    /// <param name="type">The target type to parse to.</param>
    /// <param name="field">The field descriptor containing format information.</param>
    /// <returns>A cache entry containing the throwing parser and its matching TryParse delegate, when available.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the parser locator cannot be instantiated.</exception>
    private SpanParser.ParserEntry CreateParser(Type type, FieldDescriptor field)
    {
        if (Profile.Parsers?.TryCreateFormattedParser(type, field.Format, out var parser) is true)
            return SpanParser.CreateEntry(type, parser!);

        // Nullable<T> has no Parse methods. Parse T, then lift the result to Nullable<T>.
        var parseType = Nullable.GetUnderlyingType(type) ?? type;
        var locatorType = typeof(TypeParserLocator<>).MakeGenericType(parseType);
        var locator = (ITypeParserLocator)(Activator.CreateInstance(locatorType)
            ?? throw new InvalidOperationException());

        var parameters = GetParameters(field.Format).ToArray();
        return locator.CreateEntry(type, parameters);

        static IEnumerable<object> GetParameters(object? format)
        {
            switch (format)
            {
                case TemporalFormatDescriptor temporalFormat:
                    yield return temporalFormat.Pattern;
                    yield return temporalFormat.Culture;
                    yield return DateTimeStyles.None;
                    break;

                case NumericFormatDescriptor numericFormat:
                    yield return numericFormat.Style;
                    yield return numericFormat.Culture;
                    break;

                case CustomFormatDescriptor customFormat:
                    yield return customFormat.Pattern;
                    yield return customFormat.Culture;
                    break;

                default: break;
            }
        }
    }
}
