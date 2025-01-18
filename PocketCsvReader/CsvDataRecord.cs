using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using PocketCsvReader.Configuration;
using System.Reflection;
using System.Xml.Linq;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;
public class CsvDataRecord : CsvRawRecord, IDataRecord
{
    protected TypeIndexer TypeFunctions = new();

    public CsvDataRecord(CsvProfile profile)
        : base(profile)
    {
        TypeFunctions.Register(GetByte);
        TypeFunctions.Register(GetChar);
        TypeFunctions.Register(GetString);
        TypeFunctions.Register(GetBoolean);
        TypeFunctions.Register(GetInt16);
        TypeFunctions.Register(GetInt32);
        TypeFunctions.Register(GetInt64);
        TypeFunctions.Register(GetFloat);
        TypeFunctions.Register(GetDouble);
        TypeFunctions.Register(GetDecimal);
        TypeFunctions.Register(GetGuid);
        TypeFunctions.Register(GetDate);
        TypeFunctions.Register(GetTime);
        TypeFunctions.Register(GetDateTime);
        TypeFunctions.Register(GetDateTimeOffset);
    }

    public void Register<T>(Func<string?, IFormatProvider?>? format = null) where T : IParsable<T>
    {
        IFormatProvider? provide(int i) => format is not null && TryGetFieldDescriptor(i, out var field) ? format(field.Format) : null;
        TypeFunctions.Register((i) => T.Parse(GetValueOrThrow(i).Value.ToString(), provide(i) ?? CultureInfo.InvariantCulture));
    }

    public void Register<T>(Func<string, T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        TypeFunctions.Register((i) => parse(GetValueOrThrow(i).Value.ToString()));
    }

    internal CsvDataRecord(RecordMemory record, CsvProfile? profile = null)
        : this(profile ?? CsvProfile.CommaDoubleQuote)
    {
        Record = record;

        int i =0;
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
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
            return DateTime.ParseExact(GetValueOrThrow(i), field.Format, CultureInfo.InvariantCulture);
        return DateTime.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public DateOnly GetDate(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
            return DateOnly.ParseExact(GetValueOrThrow(i), field.Format, CultureInfo.InvariantCulture);
        return DateOnly.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public TimeOnly GetTime(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
            return TimeOnly.ParseExact(GetValueOrThrow(i), field.Format, CultureInfo.InvariantCulture);
        return TimeOnly.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public DateTimeOffset GetDateTimeOffset(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
            return DateTimeOffset.ParseExact(GetValueOrThrow(i), field.Format, CultureInfo.InvariantCulture);
        return DateTimeOffset.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public decimal GetDecimal(int i) => decimal.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetFormatProvider(i));
    public double GetDouble(int i) => double.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetFormatProvider(i));
    public float GetFloat(int i) => float.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetFormatProvider(i));
    public Guid GetGuid(int i) => Guid.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public short GetInt16(int i) => short.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetFormatProvider(i));
    public int GetInt32(int i) => int.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetFormatProvider(i));
    public long GetInt64(int i) => long.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetFormatProvider(i));

    public object GetValue(int i)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");
        if (i < Fields!.Length && i >= Record!.FieldSpans.Length)
            return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;

        if (!TryGetFieldDescriptor(i, out var field)
             || !TypeFunctions.TryGetFunction(field.RuntimeType, out var func))
            return GetString(i);

        try
        {
            var value = func.DynamicInvoke(i)!;
            return value;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    public T GetFieldValue<T>(int i)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");

        if (Nullable.GetUnderlyingType(typeof(T)) != null || !typeof(T).IsValueType)
            if (IsDBNull(i))
                return default!;

        if (TypeFunctions.TryGetFunction<T>(out var func))
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
