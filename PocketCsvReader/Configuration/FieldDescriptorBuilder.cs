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
    private Dictionary<Type, FormatDescriptorBuilder> DefaultFormatBuilders = new();
    protected Type _runtimeType;
    protected FormatDescriptorBuilder? _format;
    protected string? _name;
    protected SequenceCollection? _sequences;
    protected string? _dataSourceTypeName;

    protected internal FieldDescriptorBuilder(Type runtimeType)
    {
        _runtimeType = runtimeType;
        Initialize();
    }

    private void Initialize()
    {
        DefaultFormatBuilders.Add(typeof(short), new IntegerFormatDescriptorBuilder());
        DefaultFormatBuilders.Add(typeof(int), new IntegerFormatDescriptorBuilder());
        DefaultFormatBuilders.Add(typeof(long), new IntegerFormatDescriptorBuilder());

        DefaultFormatBuilders.Add(typeof(float), new NumberFormatDescriptorBuilder());
        DefaultFormatBuilders.Add(typeof(double), new NumberFormatDescriptorBuilder());
        DefaultFormatBuilders.Add(typeof(decimal), new NumberFormatDescriptorBuilder());

        DefaultFormatBuilders.Add(typeof(DateTime), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss"));
        DefaultFormatBuilders.Add(typeof(DateTimeOffset), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss"));
        DefaultFormatBuilders.Add(typeof(DateOnly), new TemporalFormatDescriptorBuilder("yyyy-MM-dd"));
        DefaultFormatBuilders.Add(typeof(TimeOnly), new TemporalFormatDescriptorBuilder("yyyy-MM-ddTHH:mm:ss zzz"));
    }

    public FieldDescriptorBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public FieldDescriptorBuilder WithSequence(string pattern, string? value)
    {
        (_sequences ??= new()).Add(pattern, value);
        return this;
    }

    public FieldDescriptorBuilder WithDataSourceTypeName(string typeName)
    {
        _dataSourceTypeName = typeName;
        return this;
    }


    private FormatDescriptorBuilder GetDefaultFormat()
    {
        if (DefaultFormatBuilders.TryGetValue(_runtimeType, out var builder))
            return builder;
        return FormatDescriptorBuilder.None;
    }

    public virtual FieldDescriptor Build()
    {
        return new FieldDescriptor(_runtimeType, _name, (_format ?? GetDefaultFormat()).Build(), _sequences?.ToImmutable(), _dataSourceTypeName ?? string.Empty);
    }
}
