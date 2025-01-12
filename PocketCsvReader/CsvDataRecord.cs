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
        {
            var netFormat = new DateTimeFormatConverter().Convert(field.Format);
            return DateTime.ParseExact(GetValueOrThrow(i), netFormat, CultureInfo.InvariantCulture);
        }

        return DateTime.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public DateOnly GetDate(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
        {
            var netFormat = new DateTimeFormatConverter().Convert(field.Format);
            return DateOnly.ParseExact(GetValueOrThrow(i), netFormat, CultureInfo.InvariantCulture);
        }

        return DateOnly.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public TimeOnly GetTime(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
        {
            var netFormat = new DateTimeFormatConverter().Convert(field.Format);
            return TimeOnly.ParseExact(GetValueOrThrow(i), netFormat, CultureInfo.InvariantCulture);
        }

        return TimeOnly.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public DateTimeOffset GetDateTimeOffset(int i)
    {
        if (TryGetFieldDescriptor(i, out var field) && !string.IsNullOrWhiteSpace(field.Format))
        {
            var netFormat = new DateTimeFormatConverter().Convert(field.Format);
            return DateTimeOffset.ParseExact(GetValueOrThrow(i), netFormat, CultureInfo.InvariantCulture);
        }

        return DateTimeOffset.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    }

    public decimal GetDecimal(int i) => decimal.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetCulture(i));
    public double GetDouble(int i) => double.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetCulture(i));
    public float GetFloat(int i) => float.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetCulture(i));
    public Guid GetGuid(int i) => Guid.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public short GetInt16(int i) => short.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetCulture(i));
    public int GetInt32(int i) => int.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetCulture(i));
    public long GetInt64(int i) => long.Parse(GetValueOrThrow(i), GetNumericStyle(i), GetCulture(i));
    
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

        if (!TypeFunctions.TryGetFunction<T>(out var func))
            throw new NotImplementedException($"No function registered for type {typeof(T).Name}");

        return func.Invoke(i);
    }

    public int GetValues(object[] values) => throw new NotImplementedException();

    public bool IsDBNull(int i)
        => IsNull(i);
}
