﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using PocketCsvReader.Configuration;
using System.Reflection;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;
public class CsvDataRecord : CsvRawRecord, IDataRecord
{
    private TypeIndexer TypeParsers = new();
    protected Dictionary<int, ParseFunction> FieldParsers = new();

    public CsvDataRecord(CsvProfile profile)
        : base(profile)
    {
        TypeParsers.Register(GetByte);
        TypeParsers.Register(GetChar);
        TypeParsers.Register(GetString);
        TypeParsers.Register(GetBoolean);
        TypeParsers.Register(GetInt16);
        TypeParsers.Register(GetInt32);
        TypeParsers.Register(GetInt64);
        TypeParsers.Register(GetFloat);
        TypeParsers.Register(GetDouble);
        TypeParsers.Register(GetDecimal);
        TypeParsers.Register(GetGuid);
        TypeParsers.Register(GetDate);
        TypeParsers.Register(GetTime);
        TypeParsers.Register(GetDateTime);
        TypeParsers.Register(GetDateTimeOffset);

        foreach (var parser in profile.Parsers ?? [])
        {
            string getValue(int i) => GetValueOrThrow(i).Value.ToString();
            object parse(int i) => parser.Value(getValue(i));
            TypeParsers.Register(parser.Key, parse);
        }
    }

    internal CsvDataRecord(RecordMemory record, CsvProfile? profile = null)
        : this(profile ?? CsvProfile.CommaDoubleQuote)
    {
        Record = record;

        int i = 0;
        Fields = record.FieldSpans.Select(_ => $"field_{i++}").ToArray();
    }

    public object this[int i]
    {
        get => GetValue(i);
    }

    public object this[string name]
    {
        get
        {
            if (Fields is null)
                throw new InvalidOperationException("Fields are not defined yet.");
            var index = Array.IndexOf(Fields, name);
            return GetValue(index);
        }
    }

    public bool GetBoolean(int i) => bool.Parse(GetValueOrThrow(i));
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i) => throw new NotImplementedException();

    public DateTime GetDateTime(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is TemporalFormatDescriptor format)
            return DateTime.ParseExact(GetValueOrThrow(i), format.Pattern, format.Culture);

        return DateTime.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public DateOnly GetDate(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is TemporalFormatDescriptor format)
            return DateOnly.ParseExact(GetValueOrThrow(i), format.Pattern, format.Culture);
        return DateOnly.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public TimeOnly GetTime(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is TemporalFormatDescriptor format)
            return TimeOnly.ParseExact(GetValueOrThrow(i), format.Pattern, format.Culture);
        return TimeOnly.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public DateTimeOffset GetDateTimeOffset(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is TemporalFormatDescriptor format)
            return DateTimeOffset.ParseExact(GetValueOrThrow(i), format.Pattern, format.Culture);
        return DateTimeOffset.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public Guid GetGuid(int i) => Guid.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);

    public decimal GetDecimal(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor format)
            return decimal.Parse(GetValueOrThrow(i), format.Style, format.Culture);
        return decimal.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public double GetDouble(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor format)
            return double.Parse(GetValueOrThrow(i), format.Style, format.Culture);
        return double.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public float GetFloat(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor format)
            return float.Parse(GetValueOrThrow(i), format.Style, format.Culture);
        return float.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }
    public short GetInt16(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor format)
            return short.Parse(GetValueOrThrow(i), format.Style, format.Culture);
        return short.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }
    public int GetInt32(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor format)
            return int.Parse(GetValueOrThrow(i), format.Style, format.Culture);
        return int.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }
    public long GetInt64(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && field.Format is NumericFormatDescriptor format)
            return long.Parse(GetValueOrThrow(i), format.Style, format.Culture);
        return long.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public object GetValue(int i)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");
        if (i < Fields!.Length && i >= Record!.FieldSpans.Length)
            return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;

        if (!TryGetFieldDescriptor(i, out var field))
            return GetString(i);

        Func<int, object>? parse = field.Parse is not null
                                    ? FieldParsers.TryGetValue(i, out var fparse)
                                        ? (int i) => fparse
                                        : RegisterParser(i, field.Parse)
                                    : TypeParsers.TryGetParser(field.RuntimeType, out var dlg)
                                        ? (int i) => dlg.Invoke(i)!
                                        : RegisterFunction(field);
        try
        {
            var value = parse!(i);
            return value;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }


    private Func<int, object>? RegisterParser(int i, ParseFunction parse)
    {
        FieldParsers.Add(i, parse);
        return (int i) => parse.Invoke(GetValueOrThrow(i).Value.ToString());
    }

    private Func<int, object>? RegisterFunction(FieldDescriptor field)
    {
        var type = typeof(TypeParserLocator<>).MakeGenericType(field.RuntimeType);
        var locator = (ITypeParserLocator)(Activator.CreateInstance(type) ?? throw new InvalidOperationException());
        var parameters = GetParameters(field.Format).ToArray();

        IEnumerable<object> GetParameters(object? format)
        {
            switch (format)
            {
                case TemporalFormatDescriptor temporalFormat:
                    yield return temporalFormat.Pattern;
                    yield return temporalFormat.Culture;
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
        var func = locator.Locate(parameters);
        string getValue(int i) => GetValueOrThrow(i).Value.ToString();

        var parse = (int i) => func.Invoke(getValue(i))!;
        TypeParsers.Register(field!.RuntimeType, parse);
        return parse;
    }

    public T GetFieldValue<T>(int i)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");

        if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
            if (IsDBNull(i))
                return default!;

        if (TypeParsers.TryGetParser<T>(out var func))
            return func.Invoke(i);

        throw new NotImplementedException($"No function registered for type {typeof(T).Name}");
    }

    public T GetFieldValue<T>(int i, IFormatProvider format) where T : IParsable<T>
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");

        if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
            if (IsDBNull(i))
                return default!;

        return T.Parse(GetValueOrThrow(i).Value.ToString(), format);
    }

    public T GetFieldValue<T>(int i, string pattern, IFormatProvider? format=null) where T : IParsable<T>
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");

        if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
            if (IsDBNull(i))
                return default!;

        var locator = new TypeParserLocator<T>();
        var func = locator.Locate([pattern, format ?? CultureInfo.InvariantCulture]);

        return func(GetValueOrThrow(i).Value.ToString());
    }

    public T GetFieldValue<T>(int i, Func<string, T> parse)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");

        if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
            if (IsDBNull(i))
                return default!;

        return parse(GetValueOrThrow(i).Value.ToString());
    }

    public int GetValues(object[] values) => throw new NotImplementedException();

    public bool IsDBNull(int i)
        => IsNull(i);
}
