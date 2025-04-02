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
public abstract class BaseRawRecord<P> where P : IProfile
{
    protected P Profile { get; }
    private StringMapper StringMapper { get; }
    public int RowCount { get; protected set; } = 0;
    public string[]? Fields { get; protected set; } = null;
    protected RecordMemory? Record { get; set; } = null;

    protected BaseRawRecord(P profile, StringMapper stringMapper)
    {
        Profile = profile;
        StringMapper = stringMapper;    
    }

    public abstract int FieldCount { get; }
    
    public string GetName(int i)
        => Fields?[i] ?? throw new InvalidOperationException("Fields are not defined yet.");

    public virtual int GetOrdinal(string name)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");
        var index = Array.IndexOf(Fields, name);
        if (index < 0)
            throw new IndexOutOfRangeException($"Field '{name}' not found.");
        return index;
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

    protected FieldDescriptor GetFieldDescriptor(int i)
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
            field = i < Profile.Schema.Fields.Length
                        ? Profile.Schema.Fields[i] : null;
            return field is not null;
        }
        throw new NotImplementedException("Schema matching is not defined.");
    }

#if DEBUG
    public bool IsQuotedField(int i)
        => Record!.FieldSpans[i].WasQuoted;

    public bool IsEscapedField(int i)
        => Record!.FieldSpans[i].IsEscaped;
#endif

    public abstract string GetRawString(int i);

    protected bool IsNull(int i)
        => !GetValueOrThrow(i).HasValue;
    public string GetString(int i)
        => StringMapper.Map(GetValueOrThrow(i))!;

    protected virtual IFormatDescriptor GetFormat(int i)
    {
        if (!TryGetFieldDescriptor(i, out var field))
            return IFormatDescriptor.None;
        return field.Format ?? IFormatDescriptor.None;
    }
    protected abstract NullableSpan GetValueOrThrow(int i);
}
