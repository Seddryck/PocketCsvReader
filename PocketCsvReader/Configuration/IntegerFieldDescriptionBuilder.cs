using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class IntegerFieldDescriptorBuilder : FieldDescriptorBuilder
{
    internal IntegerFieldDescriptorBuilder(Type runtimeType)
        : base(runtimeType) { }

    public IntegerFieldDescriptorBuilder WithFormat(Func<IntegerFormatDescriptorBuilder, IntegerFormatDescriptorBuilder> func)
    {
        Format = func(new IntegerFormatDescriptorBuilder());
        return this;
    }

    public new IntegerFieldDescriptorBuilder WithName(string value)
        => (IntegerFieldDescriptorBuilder)base.WithName(value);

    public new IntegerFieldDescriptorBuilder WithSequence(string pattern, string? value)
        => (IntegerFieldDescriptorBuilder)base.WithSequence(pattern, value);

    public new IntegerFieldDescriptorBuilder WithDataSourceTypeName(string typeName)
        => (IntegerFieldDescriptorBuilder)base.WithDataSourceTypeName(typeName);
    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public new IntegerFieldDescriptorBuilder WithParser<T>(ParseSpan<T> parse)
        => (IntegerFieldDescriptorBuilder)base.WithParser(parse);

    public new IntegerFieldDescriptorBuilder WithParser(ParseFunction parse)
        => (IntegerFieldDescriptorBuilder)base.WithParser(parse);
}
