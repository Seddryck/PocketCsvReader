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
    private Dictionary<int, ISanitizer>? _sanitizers;
    private SanitizerFactory? _sanitizerFactory;

    protected BaseDataRecord(P profile, StringMapper stringMapper)
        : base(profile, stringMapper)
    {
        foreach (var parser in profile.Parsers ?? [])
        {
            var spanParam = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
            // Convert span to string: span.ToString()
            var toStringCall = Expression.Call(spanParam, typeof(ReadOnlySpan<char>).GetMethod(nameof(ReadOnlySpan<char>.ToString), Type.EmptyTypes)!);

            // Call the existing parser.Value(string)
            var parserFunc = Expression.Constant(parser.Value); // Func<string, X>
            var invokeParser = Expression.Invoke(parserFunc, toStringCall);

            // Cast the result to the expected return type (if needed)
            var castResult = Expression.Convert(invokeParser, parser.Key); // parser.Key is typeof(X)

            var delegateType = typeof(ParseSpan<>).MakeGenericType(parser.Key);
            var lambda = Expression.Lambda(delegateType, castResult, spanParam);

            var parse = lambda.Compile();
            Parser.Register(parser.Key, parse);
        }
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
            if (Parser.TryParse<T>(i, GetValueOrThrow(i), out var value))
                return value;

            if (TryGetFieldDescriptor(i, out var field) && field.Format is TemporalFormatDescriptor)
            {
                Parser.Register(i, typeof(T), CreateParser(typeof(T), field));
                return Parser.Parse<T>(i, GetValueOrThrow(i));
            }

            return Parser.Parse<T>(GetValueOrThrow(i));
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
            if (Parser.TryParse<Guid>(i, GetValueOrThrow(i), out var value))
                return value;

            return Parser.Parse<Guid>(GetValueOrThrow(i));
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
        try
        {
            if (Parser.TryParse<T>(i, GetValueOrThrow(i), out var value))
                return value;

            if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor)
            {
                Parser.Register(i, typeof(T), CreateParser(typeof(T), field));
                return Parser.Parse<T>(i, GetValueOrThrow(i));
            }

            return Parser.Parse<T>(GetValueOrThrow(i));
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    protected virtual object GetMissingField()
        => string.Empty;

    public object GetValue(int i)
    {
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        if (i >= Record!.FieldSpans.Length)
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
            return RegisterFieldParser(i, field);
        Parser.TryGetParser(field.RuntimeType, out ParseSpan<object>? parse);
        return parse;
    }

    private static bool HasCustomParser(FieldDescriptor field)
        => field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor);

    private ParseSpan<object> RegisterFieldParser(int i, FieldDescriptor field)
    {
        ParseSpan<object>? parse = null;
        if (field.Parse is not null)
        {
            parse = (ReadOnlySpan<char> span) => field.Parse.Invoke(span.ToString());
            Parser.Register(i, parse);
        }
        else if (field.RuntimeType == typeof(string))
        {
            parse = (ReadOnlySpan<char> span) => span.ToString();
            Parser.Register(i, parse);
        }
        else if ((field.Format is not null && field.Format is not NoneFormatDescriptor) || field.RuntimeType != typeof(object))
        {
            Parser.Register(i, field.RuntimeType, CreateParser(field.RuntimeType, field));
            if (!Parser.TryGetParser(i, out parse))
                throw new InvalidOperationException($"No parser registered for index '{i}'.");
        }
        else
            throw new ArgumentException($"Field descriptor for index '{i}' is missing both the Parse function and the Format property.");

        return parse;
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
        if (IsNullFieldValue<T>(i))
            return default!;
        try
        {
            if (Parser.TryParse<T>(i, GetValueOrThrow(i), out var value))
                return value;
            if (TryGetFieldDescriptor(i, out var field) && (field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor)))
            {
                RegisterFieldParser(i, field);
                return Parser.Parse<T>(i, GetValueOrThrow(i));
            }
            return Parser.Parse<T>(GetValueOrThrow(i));
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }

        throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
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
        var func = locator.Locate([pattern, format ?? CultureInfo.InvariantCulture]);

        return func(GetValueOrThrow(i).Value.ToString());
    }

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
        if (i >= Record!.FieldSpans.Length)
            return null;

        var field = Record.FieldSpans[i];
        if (field.Children is null)
            throw new NotImplementedException();
        return field;
    }

    private ParseSpan<T> ResolveArrayParser<T>(int i)
    {
        if (Parser.TryGetParser<T>(i, out var parse))
            return parse;
        if (TryGetFieldDescriptor(i, out var field) && (field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor)))
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
        if (TryGetFieldDescriptor(i, out var field) && (field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor)))
        {
            RegisterFieldParser(i, field);
            if (Parser.TryGetParser(i, out parse))
                return parse;
        }
        if (Parser.TryGetParser(out parse))
            return parse;
        return (ReadOnlySpan<char> span) => span.ToString();
    }

    private ReadOnlySpan<char> GetFieldSpan(FieldSpan field)
    {
        if (field.DecodedValue is not null)
            return field.DecodedValue.AsSpan();
        var record = Record ?? throw new InvalidOperationException("Current record is not set.");
        return record.Span.Slice(field.Value.Start, field.Value.Length).Span;
    }

    /// <summary>
    /// Creates a delegate that parses a <see cref="ReadOnlySpan{char}"/> into the specified type using the field's format descriptor.
    /// </summary>
    /// <param name="type">The target type to parse to.</param>
    /// <param name="field">The field descriptor containing format information.</param>
    /// <returns>A delegate that parses a span into the specified type.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the parser locator cannot be instantiated.</exception>
    private static Delegate CreateParser(Type type, FieldDescriptor field)
    {
        var locatorType = typeof(TypeParserLocator<>).MakeGenericType(type);
        var locator = (ITypeParserLocator)(Activator.CreateInstance(locatorType)
            ?? throw new InvalidOperationException());

        var parameters = GetParameters(field.Format).ToArray();
        var func = locator.Locate(parameters); // Func<string, object>

        // Build: (ReadOnlySpan<char> span) => (T)func(span.ToString())
        var spanParam = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var funcConst = Expression.Constant(func);
        var toStringCall = Expression.Call(spanParam, typeof(ReadOnlySpan<char>).GetMethod("ToString", Type.EmptyTypes)!);
        var invokeFunc = Expression.Invoke(funcConst, toStringCall);
        var castResult = Expression.Convert(invokeFunc, type);

        var delegateType = typeof(ParseSpan<>).MakeGenericType(type);
        var lambda = Expression.Lambda(delegateType, castResult, spanParam);
        return lambda.Compile();

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
