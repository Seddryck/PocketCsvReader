using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class TemporalFieldDescriptorBuilder : FieldDescriptorBuilder
{
    internal TemporalFieldDescriptorBuilder(Type runtimeType)
        : base(runtimeType) { }

    public TemporalFieldDescriptorBuilder WithFormat(string pattern, Func<TemporalFormatDescriptorBuilder, TemporalFormatDescriptorBuilder>? func = null)
    {
        Format = func is null
                    ? new TemporalFormatDescriptorBuilder(pattern)
                    : func(new TemporalFormatDescriptorBuilder(pattern));
        return this;
    }

    public new TemporalFieldDescriptorBuilder WithName(string value)
        => (TemporalFieldDescriptorBuilder)base.WithName(value);

    public new TemporalFieldDescriptorBuilder WithSequence(string pattern, string? value)
        => (TemporalFieldDescriptorBuilder)base.WithSequence(pattern, value);

    public new TemporalFieldDescriptorBuilder WithDataSourceTypeName(string typeName)
        => (TemporalFieldDescriptorBuilder)base.WithDataSourceTypeName(typeName);

    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public new TemporalFieldDescriptorBuilder WithParser<T>(ParseSpan<T> parse)
        => (TemporalFieldDescriptorBuilder)base.WithParser(parse);

    public new TemporalFieldDescriptorBuilder WithParser(ParseFunction parse)
        => (TemporalFieldDescriptorBuilder)base.WithParser(parse);
}
