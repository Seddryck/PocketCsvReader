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
public class CsvRawRecord
{
    protected CsvProfile Profile { get; }
    private StringMapper StringMapper { get; }
    public int RowCount { get; protected set; } = 0;
    public string[]? Fields { get; protected set; } = null;
    protected RecordMemory? Record { get; set; } = null;

    public CsvRawRecord(CsvProfile profile)
    {
        Profile = profile;
        StringMapper = new StringMapper(Profile.ParserOptimizations.PoolString);
    }

    public int FieldCount => Fields?.Length ?? throw new InvalidOperationException("Fields are not defined yet.");
    
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

    public string GetRawString(int i)
        => Record!.FieldSpans[i].WasQuoted
            ? $"{Profile.Dialect.QuoteChar}{Record!.Slice(i)}{Profile.Dialect.QuoteChar}"
            : Record!.Slice(i).ToString();

    protected bool IsNull(int i)
        => !GetValueOrThrow(i).HasValue;
    public string GetString(int i)
        => StringMapper.Map(GetValueOrThrow(i))!;

    private Dictionary<int, IFormatProvider> CacheFormatProviders { get; } = [];

    protected virtual IFormatProvider GetFormatProvider(int i)
    {
        if (!CacheFormatProviders.TryGetValue(i, out var provider))
        {
            if (TryGetFieldDescriptor(i, out var field) && field is NumericFieldDescriptor numericField)
            {
                var numberFormat = CultureInfo.InvariantCulture.NumberFormat;
                if ((numericField.DecimalChar is not null && numericField.DecimalChar.ToString() != numberFormat.NumberDecimalSeparator)
                    || (numericField.GroupChar is not null && numericField.GroupChar.ToString() != numberFormat.NumberGroupSeparator))
                {
                    var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                    culture.NumberFormat.NumberDecimalSeparator = numericField.DecimalChar?.ToString() ?? numberFormat.NumberDecimalSeparator;
                    culture.NumberFormat.NumberGroupSeparator = numericField.GroupChar?.ToString() ?? numberFormat.NumberGroupSeparator;
                    provider = culture;
                }
                else
                    provider = CultureInfo.InvariantCulture;
            }
            else
                provider = CultureInfo.InvariantCulture;
            CacheFormatProviders.Add(i, provider);
        }
        return provider;
    }

    protected virtual NumberStyles GetNumericStyle(int i)
    {
        var style = NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent | NumberStyles.AllowDecimalPoint;
        if (TryGetFieldDescriptor(i, out var field) && field is NumericFieldDescriptor numericField)
        {
            if (numericField.GroupChar is not null)
                style |= NumberStyles.AllowThousands;
        }
        return style;
    }

    private SanitizerFactory? sanitizerFactory;
    private Dictionary<int, ISanitizer> CacheSanitizers { get; } = [];
    protected NullableSpan GetValueOrThrow(int i)
    {
        if (i < Record!.FieldSpans.Length)
        {
            sanitizerFactory ??= new SanitizerFactory(Profile);
            var sanitizer = CacheSanitizers.GetOrAdd(i,
                sanitizerFactory.Create(SequenceCollection.Concat(Profile.Resource?.Sequences, (Profile.Schema is null ? null : GetFieldDescriptor(i))?.Sequences)
                                            , new FieldEscaper(Profile)
                ));
            return sanitizer.Sanitize(Record!.Slice(i).Span, Record!.FieldSpans[i].IsEscaped, Record!.FieldSpans[i].WasQuoted);
        }
        if (i < Fields!.Length && Profile.ParserOptimizations.ExtendIncompleteRecords)
            return new NullableSpan(Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty);
        throw new IndexOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.FieldSpans.Length} defined fields.");
    }
}
