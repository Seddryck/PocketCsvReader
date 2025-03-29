using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public class FieldDescriptorBuilder
{
    private readonly Dictionary<Type, FormatDescriptorBuilder> defaultFormatBuilders = [];
    protected Type runtimeType;
    protected FormatDescriptorBuilder? format;
    protected ParseFunction? parse;
    protected string? name;
    protected SequenceCollection? sequences;
    protected string? dataSourceTypeName;

    protected internal FieldDescriptorBuilder(Type runtimeType)
    {
        this.runtimeType = runtimeType;
        Initialize();
    }

    private void Initialize()
    {
        defaultFormatBuilders.Add(typeof(short), new IntegerFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(int), new IntegerFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(long), new IntegerFormatDescriptorBuilder());

        defaultFormatBuilders.Add(typeof(float), new NumberFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(double), new NumberFormatDescriptorBuilder());
        defaultFormatBuilders.Add(typeof(decimal), new NumberFormatDescriptorBuilder());

        defaultFormatBuilders.Add(typeof(DateTime), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss"));
        defaultFormatBuilders.Add(typeof(DateTimeOffset), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss"));
        defaultFormatBuilders.Add(typeof(DateOnly), new TemporalFormatDescriptorBuilder("yyyy-MM-dd"));
        defaultFormatBuilders.Add(typeof(TimeOnly), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss zzz"));
    }

    public FieldDescriptorBuilder WithName(string name)
    {
        this.name = name;
        return this;
    }

    public FieldDescriptorBuilder WithSequence(string pattern, string? value)
    {
        (sequences ??= []).Add(pattern, value);
        return this;
    }

    public FieldDescriptorBuilder WithDataSourceTypeName(string typeName)
    {
        dataSourceTypeName = typeName;
        return this;
    }

    public FieldDescriptorBuilder WithParser(ParseFunction parse)
    {
        this.parse = parse;
        return this;
    }

    private FormatDescriptorBuilder GetDefaultFormat()
    {
        if (defaultFormatBuilders.TryGetValue(runtimeType, out var builder))
            return builder;
        return FormatDescriptorBuilder.None;
    }

    public virtual FieldDescriptor Build()
    {
        return new FieldDescriptor(runtimeType, name, (format ?? GetDefaultFormat()).Build(), parse, sequences?.ToImmutable(), dataSourceTypeName ?? string.Empty);
    }
}
