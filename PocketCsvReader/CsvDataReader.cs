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

namespace PocketCsvReader;
public class CsvDataReader : IDataReader
{
    private TypeIndexer TypeFunctions = new();

    private bool _isClosed = false;
    private RecordParser? RecordParser { get; set; }
    private CsvProfile Profile { get; }
    private Stream Stream { get; }
    private StreamReader? StreamReader { get; set; }
    private Memory<char> Buffer { get; set; }
    private StringMapper StringMapper { get; }
    private EncodingInfo? FileEncoding { get; set; }

    private bool IsEof { get; set; } = false;
    public int RowCount { get; private set; } = 0;
    private int BufferSize { get; set; } = 64 * 1024;

    public string[]? Fields { get; private set; } = null;
    public RecordMemory? Record { get; private set; } = null;

    public CsvDataReader(Stream stream, CsvProfile profile)
    {
        Stream = stream;
        Buffer = new Memory<char>(new char[BufferSize]);
        Profile = profile;
        StringMapper = new StringMapper(Profile);

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

    public void Initialize()
    {
        FileEncoding ??= new EncodingDetector().GetStreamEncoding(Stream);
        StreamReader = new StreamReader(Stream, FileEncoding!.Encoding, false);
        var bufferBOM = new char[1];
        StreamReader.Read(bufferBOM, 0, bufferBOM.Length);
        StreamReader.Rewind();

        if (FileEncoding!.BomBytesCount > 0)
            StreamReader.BaseStream.Position = FileEncoding!.BomBytesCount;

        IsEof = false;
        RowCount = 0;
        RecordParser = new RecordParser(StreamReader, Profile);
    }

    public bool Read()
    {
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        if (RowCount == 0)
            if (RecordParser!.Profile.Dialect.Header)
                RegisterHeader(RecordParser!.ReadHeaders(), "field_");

        IsEof = RecordParser!.ReadNextRecord(out RecordSpan rawRecord);
        if (RowCount == 0 && !RecordParser!.Profile.Dialect.Header)
            RegisterHeader([(string?[])Array.CreateInstance(typeof(string), rawRecord.FieldSpans.Length)], "field_");

        if (rawRecord.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }
        else
            Record = rawRecord.AsMemory();

        RowCount++;

        HandleUnexpectedFields(Fields!.Length);

        return true;
    }

    private void RegisterHeader(string?[][] headers, string unamedPrefix)
    {
        var maxField = headers.Select(x => x.Length).Max();
        var names = (string[])Array.CreateInstance(typeof(string), maxField);

        foreach (var header in headers)
        {
            var last = string.Empty;
            for (int i = 0; i < maxField; i++)
            {
                if (i < header.Length && !string.IsNullOrEmpty(header[i]))
                    last = header[i];
                names[i] = string.IsNullOrEmpty(names[i])
                            ? $"{last}"
                            : $"{names[i]}{Profile.Dialect.HeaderJoin}{last}";
            }
        }
        int unnamedFieldIndex = 0;
        Fields = (RecordParser!.Profile.Dialect.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{unamedPrefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{unamedPrefix}{unnamedFieldIndex++}")).ToArray();
    }

    private void HandleUnexpectedFields(int expectedLength)
    {
        var length = Record!.FieldSpans.Length;
        if (expectedLength < length)
            throw new InvalidDataException
            (
                string.Format
                (
                    "The record {0} contains {1} more field{2} than expected."
                    , RowCount + 1 + Convert.ToInt32(RecordParser!.Profile.Dialect.Header)
                    , length - expectedLength
                    , length - expectedLength > 1 ? "s" : string.Empty
                )
            );
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

    public int Depth => 1;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => 0;

    public int FieldCount => Fields?.Length ?? throw new InvalidOperationException("Fields are not defined yet.");

    public bool GetBoolean(int i) => bool.Parse(GetValueOrThrow(i));
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i) => throw new NotImplementedException();
    public string GetDataTypeName(int i) => throw new NotImplementedException();
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

    private Dictionary<int, CultureInfo> CacheCulture { get; } = [];

    protected virtual CultureInfo GetCulture(int i)
    {
        if (!CacheCulture.TryGetValue(i, out var culture))
        {
            if (TryGetFieldDescriptor(i, out var field) && field is NumericFieldDescriptor numericField)
            {
                var numberFormat = CultureInfo.InvariantCulture.NumberFormat;
                if ((numericField.DecimalChar is not null && numericField.DecimalChar.ToString() != numberFormat.NumberDecimalSeparator)
                    || (numericField.GroupChar is not null && numericField.GroupChar.ToString() != numberFormat.NumberGroupSeparator))
                {
                    culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                    culture.NumberFormat.NumberDecimalSeparator = numericField.DecimalChar?.ToString() ?? numberFormat.NumberDecimalSeparator;
                    culture.NumberFormat.NumberGroupSeparator = numericField.GroupChar?.ToString() ?? numberFormat.NumberGroupSeparator;
                }
                else
                    culture = CultureInfo.InvariantCulture;
            }
            else
                culture = CultureInfo.InvariantCulture;
            CacheCulture.Add(i, culture);
        }
        return culture;
    }

    public decimal GetDecimal(int i) => decimal.Parse(GetValueOrThrow(i), GetCulture(i));

    public double GetDouble(int i) => double.Parse(GetValueOrThrow(i), GetCulture(i));
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i)
    {
        if (TryGetFieldDescriptor(i, out var field))
            return field.RuntimeType;
        return typeof(object);
    }

    protected FieldDescriptor GetFieldDescriptor(int i)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");

        if (Profile.Schema is null)
            throw new InvalidOperationException("Schema is not defined.");

        if (Profile.Schema.IsMatchingByName)
        {
            var headerName = GetName(i);
            if (Profile.Schema.Fields.TryGetValue(headerName, out var field))
                return field;
            throw new IndexOutOfRangeException($"Field index '{i}' is linked to header '{headerName}' but there is no corresponding field in the schema.");
        }

        if (Profile.Schema.IsMatchingByIndex)
        {
            if (i < Profile.Schema.Fields.Length)
                return Profile.Schema.Fields[i];
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");
        }

        throw new NotImplementedException("Schema matching is not defined.");
    }

    protected bool TryGetFieldDescriptor(int i, [NotNullWhen(true)] out FieldDescriptor? field)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");

        if (Profile.Schema is null)
        {
            field = null;
            return false;
        }

        if (Profile.Schema.IsMatchingByName)
            return Profile.Schema.Fields.TryGetValue(GetName(i), out field);

        if (Profile.Schema.IsMatchingByIndex)
        {
            field = i < Profile.Schema.Fields.Length
                        ? Profile.Schema.Fields[i] : null;
            return field is not null;
        }
        throw new NotImplementedException("Schema matching is not defined.");
    }

    protected virtual NumberStyles GetNumericStyle()
        => NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent;

    public float GetFloat(int i) => float.Parse(GetValueOrThrow(i), GetCulture(i));
    public Guid GetGuid(int i) => Guid.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public short GetInt16(int i) => short.Parse(GetValueOrThrow(i), GetNumericStyle(), GetCulture(i));
    public int GetInt32(int i) => int.Parse(GetValueOrThrow(i), GetNumericStyle(), GetCulture(i));
    public long GetInt64(int i) => long.Parse(GetValueOrThrow(i), GetNumericStyle(), GetCulture(i));
    public string GetName(int i)
        => Fields?[i] ?? throw new InvalidOperationException("Fields are not defined yet.");
    public int GetOrdinal(string name)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");
        var index = Array.IndexOf(Fields, name);
        if (index < 0)
            throw new IndexOutOfRangeException($"Field '{name}' not found.");
        return index;
    }

    public DataTable? GetSchemaTable() => throw new NotImplementedException();

    public string GetString(int i)
        => StringMapper.Parse(GetValueOrThrow(i)
                , i < Record!.FieldSpans.Length && Record!.FieldSpans[i].IsEscaped
                , i < Record!.FieldSpans.Length && Record!.FieldSpans[i].WasQuoted)!;
    public object GetValue(int i)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");
        if (i < Fields!.Length && i >= Record!.FieldSpans.Length)
            return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;

        if (!TryGetFieldDescriptor(i, out var field)
            || !TypeFunctions.TryGetFunction(field.RuntimeType, out var func))
            return GetString(i);

        return func.DynamicInvoke(i)!;
    }

    public T GetFieldValue<T>(int i)
    {
        if (i >= FieldCount)
            throw new IndexOutOfRangeException($"Field index '{i}' is out of range.");

        if (!TypeFunctions.TryGetFunction<T>(out var func))
            throw new NotImplementedException($"No function registered for type {typeof(T).Name}");

        return func.Invoke(i);
    }

    public int GetValues(object[] values) => throw new NotImplementedException();
    public bool IsDBNull(int i)
        => StringMapper.Parse(GetValueOrThrow(i), Record!.FieldSpans[i].IsEscaped, Record!.FieldSpans[i].WasQuoted) is null;

    public bool NextResult() => throw new NotImplementedException();

    private ReadOnlySpan<char> GetValueOrThrow(int i)
    {
        if (i < Record!.FieldSpans.Length)
            return Record.Slice(i).Span;
        if (i < Fields!.Length && Profile.ParserOptimizations.ExtendIncompleteRecords)
            return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;
        throw new IndexOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.FieldSpans.Length} defined fields.");
    }

    public void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            StreamReader?.Dispose();
            Stream?.Dispose();
            RecordParser?.Dispose();
        }
    }

    public void Dispose()
    {
        Close(); // Ensures resources are released
        GC.SuppressFinalize(this); // Prevents finalizer from running
    }

    ~CsvDataReader()
    {
        Dispose();
    }

    internal class TypeIndexer
    {
        private readonly Dictionary<Type, object> _typeToFunctionMap = new();

        public void Register<T>(Func<int, T> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            _typeToFunctionMap[typeof(T)] = func;
        }

        public bool TryGetFunction<T>([NotNullWhen(true)] out Func<int, T>? func)
        {
            if (_typeToFunctionMap.TryGetValue(typeof(T), out var value))
            {
                func = (Func<int, T>)value;
                return true;
            }
            func = null;
            return false;
        }

        public bool TryGetFunction(Type type, [NotNullWhen(true)] out Delegate? dlg)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (_typeToFunctionMap.TryGetValue(type, out var func))
            {
                dlg = (Delegate)func;
                return true;
            }
            dlg = null;
            return false;
        }


        public Delegate GetFunction(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (_typeToFunctionMap.TryGetValue(type, out var func))
                return (Delegate)func;

            throw new InvalidOperationException($"No function registered for type {type.Name}");
        }

        public Func<int, T> GetFunction<T>()
        {
            if (_typeToFunctionMap.TryGetValue(typeof(T), out var func))
                return (Func<int, T>)func;

            throw new InvalidOperationException($"No function registered for type {typeof(T).Name}");
        }
    }
}
