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
public abstract class BaseDataRecord<P> : BaseRawRecord<P>, IDataRecord where P : IProfile
{
    private SpanParser Parser { get; } = new();

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

        if (IsNull(i))
            throw new InvalidCastException($"Field index '{i}' is null.");

        static bool IsFormatDescriptor(FieldDescriptor field)
            => field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor);

        TryGetFieldDescriptor(i, out var field);
        ParseSpan<object>? parse = null;
        if (!Parser.TryGetParser(i, out parse))
            if (field is not null && IsFormatDescriptor(field))
                parse = RegisterFieldParser(i, field);
            else if (field is not null)
                Parser.TryGetParser(field.RuntimeType, out parse);

        if (parse is null)
            return GetString(i);

        try
        {
            var value = parse.Invoke(GetValueOrThrow(i));
            return value;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    private ParseSpan<object> RegisterFieldParser(int i, FieldDescriptor field)
    {
        ParseSpan<object>? parse = null;
        if (field.Parse is not null)
        {
            parse = (ReadOnlySpan<char> span) => field.Parse.Invoke(span.ToString());
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
            values[i] = IsNull(i) ? null! : GetValue(i);
        return length;
    }

    public bool IsDBNull(int i)
        => IsNull(i);

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
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        if (i >= Record!.FieldSpans.Length)
            return [];

        if (Record!.FieldSpans[i].Children is null)
            throw new NotImplementedException();

        if (!Parser.TryGetParser<T>(i, out var parse))
        {
            if (TryGetFieldDescriptor(i, out var field) && (field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor)))
            {
                RegisterFieldParser(i, field);
                parse = Parser.GetParser<T>(i);
            }
            else if (!Parser.TryGetParser(out parse))
                throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
        }

        var array = (T?[])Array.CreateInstance(typeof(T?), Record!.FieldSpans[i].Children!.Length);
        for (int j = 0; j < Record!.FieldSpans[i].Children!.Length; j++)
        {
            var child = Record!.FieldSpans[i].Children![j];
            array[j] = IsNullFieldValue<T>(i)
                        ? default
                        : parse(Record!.Span.Slice(child.Value.Start, child.Value.Length).Span);
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
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        if (i >= Record!.FieldSpans.Length)
            return [];

        if (Record!.FieldSpans[i].Children is null)
            throw new NotImplementedException();

        if (!Parser.TryGetParser(i, out var parse))
        {
            if (TryGetFieldDescriptor(i, out var field) && (field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor)))
            {
                RegisterFieldParser(i, field);
                Parser.TryGetParser(i, out parse);
            }
            else if (!Parser.TryGetParser(out parse))
                parse = (ReadOnlySpan<char> span) => span.ToString();
        }

        var array = (object?[])Array.CreateInstance(typeof(object), Record!.FieldSpans[i].Children!.Length);
        for (int j = 0; j < Record!.FieldSpans[i].Children!.Length; j++)
        {
            var child = Record!.FieldSpans[i].Children![j];
            array[j] = IsNullFieldValue<object>(i)
                ? null
                : parse!(Record!.Span.Slice(child.Value.Start, child.Value.Length).Span);
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
        if (i >= FieldCount)
            throw new ArgumentOutOfRangeException($"Field index '{i}' is out of range.");
        if (i >= Record!.FieldSpans.Length)
            throw new ArgumentOutOfRangeException($"Field index '{i}' doesn't contain an item at position '{j}'.");

        if (Record!.FieldSpans[i].Children is null)
            throw new NotImplementedException();
        if (j >= Record!.FieldSpans[i].Children!.Length)
            throw new ArgumentOutOfRangeException($"Field index '{i}' doesn't contain an item at position '{j}'.");

        if (!Parser.TryGetParser<T>(i, out var parse))
        {
            if (TryGetFieldDescriptor(i, out var field) && (field.Parse is not null || (field.Format is not null && field.Format is not NoneFormatDescriptor)))
            {
                RegisterFieldParser(i, field);
                parse = Parser.GetParser<T>(i);
            }
            else if (!Parser.TryGetParser(out parse))
                throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
        }

        var child = Record!.FieldSpans[i].Children![j];
        return IsNullFieldValue<T>(i)
                ? default
                : parse(Record!.Span.Slice(child.Value.Start, child.Value.Length).Span);
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
